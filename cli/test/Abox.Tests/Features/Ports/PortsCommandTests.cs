using Abox.Cli;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class PortsCommandTests
{
    [Theory]
    [InlineData("0", 8203)]
    [InlineData("10000", 18203)]
    [InlineData("20000", 28203)]
    public async Task NativeBindingConflictsThroughTheRealAdapterNeverPublishOrUnpublish(
        string offset,
        int hostPort
    )
    {
        // Arrange
        var runner = Substitute.For<IProcessRunner>();
        using var fixture = new PortsCommandFixture(runner: runner);
        fixture.Root.File(
            "folder/Properties/launchSettings.json",
            """{"profiles":{"app":{"applicationUrl":"http://localhost:8203"}}}"""
        );
        var calls = new List<ProcessRequest>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                calls.Add(request);
                var output = request.Arguments[0] switch
                {
                    "version" => """{"client":{"version":"0.51.0"}}""",
                    "diagnose" => """{"checks":[]}""",
                    "ls" => $$"""[{"name":"{{fixture.Name}}","status":"running"}]""",
                    "ports" => $$"""
                        [{"host_ip":"127.0.0.1","host_port":{{hostPort}},"sandbox_port":8203,"protocol":"tcp4"},
                         {"host_ip":"127.0.0.1","host_port":19000,"sandbox_port":9000,"protocol":"tcp4"}]
                        """,
                    "exec" when request.Arguments.Contains("python3") =>
                        """[{"application_port":8203,"relay_port":58203}]""",
                    "exec" => "",
                    _ => throw new InvalidOperationException("Unexpected runtime operation."),
                };
                return new ProcessResult(0, output, "");
            });

        // Act
        var exit = await fixture.Run("--offset", offset);

        // Assert
        exit.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .Contain($"conflicting runtime mapping 127.0.0.1:{hostPort}:8203/tcp4")
            .And.Contain("explicitly remove only that exact binding")
            .And.Contain($"--offset {offset}")
            .And.NotContain("Application ports published");
        calls
            .Should()
            .NotContain(call =>
                call.Arguments.Contains("--unpublish") || call.Arguments.Contains("--publish")
            );
    }

    [Fact]
    public async Task OneRunOverrideCannotBypassInvalidSharedFolderConfiguration()
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        fixture.Root.File("folder/abox.json", """{"portOffset":1e4,"userSecrets":["sample"]}""");

        // Act
        var exit = await fixture.Run("--offset", "20000");

        // Assert
        exit.Should().Be(1);
        fixture.Console.AllText.Should().Contain("portOffset").And.Contain("integer");
        fixture.Sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("0", 8203, 8203)]
    [InlineData("-8202", 8203, 1)]
    [InlineData("57332", 8203, 65535)]
    [InlineData("-65534", 65535, 1)]
    [InlineData("65534", 1, 65535)]
    public async Task ZeroNegativeAndBoundaryOffsetsPublishTheValidatedHostPort(
        string offset,
        int sandboxPort,
        int hostPort
    )
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        fixture.Root.File(
            "folder/Properties/launchSettings.json",
            $$$$"""{"profiles":{"app":{"applicationUrl":"http://localhost:{{{{sandboxPort}}}}"}}}"""
        );

        // Act
        var exit = await fixture.Run("--offset", offset);

        // Assert
        exit.Should().Be(0);
        fixture.Console.AllText.Should().Contain($"| {sandboxPort} | {hostPort}");
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                fixture.Name,
                Arg.Is<IReadOnlyList<ApplicationPortMapping>>(mappings =>
                    mappings[0].HostPort == hostPort
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("1.5")]
    [InlineData("1e4")]
    [InlineData("0x2710")]
    [InlineData("1,000")]
    [InlineData("NaN")]
    [InlineData("2147483648")]
    [InlineData("2147483647")]
    [InlineData("-2147483648")]
    [InlineData("65535")]
    [InlineData("-65535")]
    [InlineData("57333")]
    [InlineData("-8203")]
    public async Task InvalidOffsetsCannotStartASandboxOrPublishAPartialPlan(string offset)
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        fixture.Root.File(
            "folder/Properties/launchSettings.json",
            """{"profiles":{"app":{"applicationUrl":"http://localhost:1;http://localhost:8203"}}}"""
        );

        // Act
        var exit = await fixture.Run("--offset", offset);

        // Assert
        exit.Should().Be(1);
        await fixture.NoPublishing();
        await fixture
            .Sbx.DidNotReceive()
            .ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderOffsetIsSnapshottedBeforeRuntimeAccessAndOverrideDoesNotLeakToTheNextRun(
        bool initiallyAbsent
    )
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        fixture.Root.File(
            "folder/Properties/launchSettings.json",
            """{"profiles":{"app":{"applicationUrl":"http://localhost:8203"}}}"""
        );
        var file = Path.Combine(fixture.Location, "abox.json");
        if (!initiallyAbsent)
            fixture.Root.File("folder/abox.json", """{"portOffset":20000}""");
        fixture
            .Sbx.GetVersionAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                File.WriteAllText(file, """{"portOffset":30000}""");
                return new Version(0, 51, 0);
            });

        // Act
        var first = await fixture.Run();
        var second = await fixture.Run("--offset", "40000");
        var third = await fixture.Run();

        // Assert
        new[] { first, second, third }
            .Should()
            .OnlyContain(exit => exit == 0);
        fixture
            .Console.AllText.Should()
            .Contain(initiallyAbsent ? "| 8203 | 18203" : "| 8203 | 28203")
            .And.Contain("| 8203 | 48203")
            .And.Contain("| 8203 | 38203");
    }

    [Theory]
    [InlineData(null, null, 18203)]
    [InlineData("{}", null, 18203)]
    [InlineData("{\"portOffset\":20000}", null, 28203)]
    [InlineData("{\"portOffset\":20000}", "30000", 38203)]
    public async Task OffsetUsesCommandThenFolderThenDefaultWithoutWritingConfiguration(
        string? configuration,
        string? offset,
        int hostPort
    )
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        fixture.Root.File(
            "folder/Properties/launchSettings.json",
            """{"profiles":{"app":{"applicationUrl":"http://localhost:8203"}}}"""
        );
        var path = Path.Combine(fixture.Location, "abox.json");
        if (configuration is not null)
            fixture.Root.File("folder/abox.json", configuration);

        // Act
        var exit = await fixture.Run(offset is null ? [] : ["--offset", offset]);

        // Assert
        exit.Should().Be(0);
        fixture.Console.AllText.Should().Contain($"| 8203 | {hostPort}");
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                fixture.Name,
                Arg.Is<IReadOnlyList<ApplicationPortMapping>>(mappings =>
                    mappings.Count == 1 && mappings[0] == new ApplicationPortMapping(8203, hostPort)
                ),
                Arg.Any<CancellationToken>()
            );
        if (configuration is null)
            File.Exists(path).Should().BeFalse();
        else
            (await File.ReadAllTextAsync(path)).Should().Be(configuration);
    }

    [Fact]
    public async Task SelectedRootMayBeLinkedButKeepsItsLexicalSandboxIdentity()
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        using var links = new DirectoryLinks();
        fixture.Root.File(
            "folder/Properties/launchSettings.json",
            """{"profiles":{"app":{"applicationUrl":"http://localhost:55535"}}}"""
        );
        var link = Path.Combine(fixture.Root.Path, "selected-root");
        await links.Add(link, fixture.Location);
        fixture
            .Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(SandboxName.For(link), "running", null) });

        // Act
        var exit = await fixture.Run("--location", link);

        // Assert
        exit.Should().Be(0);
        fixture.Console.AllText.Should().Contain("| 55535 | 65535");
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                SandboxName.For(link),
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("Properties/launchSettings.json")]
    [InlineData("Web.csproj")]
    [InlineData("Web.fsproj")]
    [InlineData("Web.vbproj")]
    public async Task MatchingFileLinksAreRejectedWithoutReadingTheTarget(string relative)
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        fixture.Root.CreateDirectory("folder/Properties");
        var target = fixture.Root.File("synthetic-target.json", "must-never-be-read");
        var path = Path.Combine(fixture.Location, relative);
        try
        {
            File.CreateSymbolicLink(path, target);
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException or PlatformNotSupportedException
                || (
                    OperatingSystem.IsWindows()
                    && exception is IOException
                    && (exception.HResult & 0xffff) == 1314
                )
            )
        {
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value
                    + "File symlinks require native link privileges on this host.",
                exception
            );
        }

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .Contain(Path.GetFileName(relative))
            .And.NotContain("must-never-be-read");
        await fixture.NoPublishing();
    }

    [Fact]
    public async Task UnreadableProjectFailsBeforePublishingWithoutSilentlySkippingHttps()
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        var path = fixture.Root.File(
            "folder/Web.csproj",
            """<Project Sdk="Microsoft.NET.Sdk.Web"><!-- never-echo-project-content --></Project>"""
        );
        fixture.Root.File(
            "folder/Properties/launchSettings.json",
            """{"profiles":{"app":{"applicationUrl":"https://localhost:8203"}}}"""
        );
        using var locked = OperatingSystem.IsWindows()
            ? new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)
            : null;
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(path, UnixFileMode.None);
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                try
                {
                    using var readable = File.OpenRead(path);
                    throw new InvalidOperationException(
                        Xunit.v3.DynamicSkipToken.Value
                            + "This user can bypass file read permissions (for example Linux root)."
                    );
                }
                catch (UnauthorizedAccessException) { }
            }

            // Act
            var exit = await fixture.Run();

            // Assert
            exit.Should().Be(1);
            fixture
                .Console.AllText.Should()
                .Contain("Web.csproj")
                .And.Contain("readable ordinary project XML")
                .And.NotContain("never-echo-project-content")
                .And.NotContain("Application ports published");
            await fixture.NoPublishing();
            await fixture
                .Sbx.DidNotReceive()
                .GetDevelopmentCertificatePasswordAsync(
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                );
        }
        finally
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    [Fact]
    public async Task InvalidUtf8IsAnActionableDiscoveryFailure()
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        var file = fixture.Root.File("folder/Properties/launchSettings.json", "");
        await File.WriteAllBytesAsync(file, [0xff, 0xfe, 0xff]);

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(1);
        fixture.Console.AllText.Should().Contain("Fix").And.Contain("launchSettings.json");
        await fixture.NoPublishing();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UsesSharedCurrentOrLegacyResolutionAndRetainsTheSelectedActualIdentity(
        bool legacy
    )
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        var legacyName = LegacySandboxName.For(fixture.Location, fixture.Location);
        fixture.Root.File(
            "folder/Properties/launchSettings.json",
            """{"profiles":{"app":{"applicationUrl":"http://localhost:8203"}}}"""
        );
        fixture
            .Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(
                legacy
                    ? new[] { new SandboxInfo(legacyName, "stopped", null) }
                    : new[] { new SandboxInfo(fixture.Name, "running", null) }
            );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(0);
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                legacy ? legacyName : fixture.Name,
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ExplicitLocationOverridesDefaultWithoutRequiringFolderConfigTeamSourcesOrBuilds()
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        var other = fixture.Root.CreateDirectory("explicit");
        fixture.Root.File(
            "explicit/Properties/launchSettings.json",
            """{"profiles":{"app":{"applicationUrl":"http://localhost:8203"}}}"""
        );
        fixture
            .Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(SandboxName.For(other), "running", null) });

        // Act
        var exit = await fixture.Run("--location", other);

        // Assert
        exit.Should().Be(0);
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                SandboxName.For(other),
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task UnconfiguredLocationUsesCurrentFolderAndMissingSandboxGivesCreateHintWithoutCreation()
    {
        // Arrange
        using var fixture = new PortsCommandFixture(new AboxConfig());

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(1);
        await fixture.Sbx.Received(1).ListSandboxesAsync(Arg.Any<CancellationToken>());
        fixture
            .Console.AllText.Should()
            .Contain("Create it with: abox create")
            .And.Contain("never creates or replaces");
        await fixture.NoPublishing();
        await fixture
            .Sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
        await fixture
            .Sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 130)]
    public async Task SandboxDiscoveryFailureDoesNotFallBackOrPublish(bool cancel, int expected)
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        fixture
            .Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<IReadOnlyList<SandboxInfo>>(
                    cancel
                        ? new OperationCanceledException()
                        : new AboxException("runtime discovery unavailable")
                )
            );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(expected);
        await fixture.NoPublishing();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"profiles\":{}}")]
    [InlineData("{\"profiles\":{\"app\":{\"applicationUrl\":\" ; ; \"}}}")]
    [InlineData(
        "{\"profiles\":{\"app\":{\"applicationUrl\":\"http://localhost;https://localhost\"}}}"
    )]
    public async Task NoExplicitPortsIsASuccessfulNoOp(string json)
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        fixture.Root.File("folder/Properties/launchSettings.json", json);

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(0);
        fixture
            .Console.AllText.Should()
            .Contain("No explicit application ports")
            .And.NotContain("Application ports published");
        await fixture.NoPublishing();
    }

    [Fact]
    public async Task PreflightFailurePreventsDiscoveryAndPublishing()
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        fixture.Sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 50, 0));

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(1);
        await fixture.NoPublishing();
        await fixture.Sbx.DidNotReceive().ListSandboxesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 130)]
    public async Task FailedOrCancelledPublishingExplainsPossiblePartialStateAndRetryWithoutClaimingSuccess(
        bool cancel,
        int expectedExit
    )
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        fixture.Root.File(
            "folder/Properties/launchSettings.json",
            """{"profiles":{"app":{"applicationUrl":"http://localhost:8203"}}}"""
        );
        fixture
            .Sbx.EnsureApplicationPortsAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromException(
                    cancel
                        ? new OperationCanceledException()
                        : new AboxException("host port 28203 is already in use")
                )
            );

        // Act
        var exit = await fixture.Run("--offset", "20000");

        // Assert
        exit.Should().Be(expectedExit);
        fixture
            .Console.AllText.Should()
            .Contain("retry")
            .And.Contain("may remain")
            .And.Contain("--offset 20000")
            .And.NotContain("Application ports published");
        if (!cancel)
            fixture.Console.AllText.Should().Contain("28203").And.Contain("already in use");
    }

    [Fact]
    public async Task DiscoveryStaysBelowLocationAndSkipsArtifactsDirectoryLinksAndCycles()
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        using var links = new DirectoryLinks();
        foreach (
            var excluded in new[]
            {
                ".git",
                "bin",
                "obj",
                "node_modules",
                "dist",
                "coverage",
                ".angular",
                ".abox",
                ".vs",
            }
        )
            fixture.Root.File(
                $"folder/{excluded}/Properties/launchSettings.json",
                "invalid excluded JSON"
            );
        fixture.Root.File("Properties/launchSettings.json", "invalid parent JSON");
        fixture.Root.File("folder/unrelated/launchSettings.json", "invalid non-Properties JSON");
        fixture.Root.File("outside/Properties/launchSettings.json", "invalid escaped JSON");
        await links.Add(
            Path.Combine(fixture.Location, "escape"),
            Path.Combine(fixture.Root.Path, "outside")
        );
        await links.Add(Path.Combine(fixture.Location, "cycle"), fixture.Location);
        fixture.Root.File(
            "folder/real/Properties/launchSettings.json",
            """{"profiles":{"app":{"applicationUrl":"http://localhost:8203"}}}"""
        );
        await links.Add(
            Path.Combine(fixture.Location, "alias"),
            Path.Combine(fixture.Location, "real")
        );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(0);
        fixture.Console.Output.Count(line => line.Contains("| 8203 | 18203")).Should().Be(1);
    }

    [Theory]
    [InlineData("oversized")]
    [InlineData("depth")]
    public async Task DiscoveryResourceLimitsFailBeforePublishingRatherThanSilentlyTruncate(
        string limit
    )
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        if (limit == "oversized")
            fixture.Root.File(
                "folder/Properties/launchSettings.json",
                new string(' ', 1024 * 1024 + 1)
            );
        else
            fixture.Root.CreateDirectory("folder/" + string.Join('/', Enumerable.Repeat("d", 65)));

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .Contain("limit")
            .And.NotContain("Application ports published");
        await fixture.NoPublishing();
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    [InlineData("{\"profiles\":[]}")]
    [InlineData("{\"profiles\":{\"bad\":false}}")]
    [InlineData("{\"profiles\":{\"bad\":{\"applicationUrl\":42}}}")]
    [InlineData("{\"profiles\":{\"bad\":{\"applicationUrl\":\"not-a-url\"}}}")]
    [InlineData("{\"profiles\":{\"bad\":{\"applicationUrl\":\"http://localhost:0\"}}}")]
    [InlineData("{\"profiles\":{\"bad\":{\"applicationUrl\":\"http://localhost:65536\"}}}")]
    [InlineData("{\"profiles\":{\"bad\":{\"applicationUrl\":\"http://localhost:55536\"}}}")]
    [InlineData("{\"profiles\":{\"bad\":{\"applicationUrl\":\"http://localhost:abc\"}}}")]
    [InlineData("{\"profiles\":{\"bad\":{\"applicationUrl\":\"ftp://localhost:21\"}}}")]
    [InlineData("{\"profiles\":{},\"profiles\":{}}")]
    [InlineData(
        "{\"profiles\":{\"bad\":{\"applicationUrl\":\"http://localhost\\\\ignored:8203\"}}}"
    )]
    public async Task InvalidDiscoveryFailsBeforeAnyPublishingAndNamesTheFileWithoutEchoingItsContents(
        string json
    )
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        fixture.Root.File(
            "folder/a-valid/Properties/launchSettings.json",
            """{"profiles":{"http":{"applicationUrl":"http://localhost:8203"}}}"""
        );
        fixture.Root.File("folder/b-invalid/Properties/launchSettings.json", json);

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .Contain("b-invalid")
            .And.Contain("launchSettings.json")
            .And.Contain("Fix")
            .And.NotContain("Application ports published")
            .And.NotContain("not-a-url");
        await fixture.NoPublishing();
    }

    [Fact]
    public async Task OnlyExplicitPortsCountIncludingExplicitSchemeDefaultsAndWildcardHosts()
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        fixture.Root.File(
            "folder/Properties/launchSettings.json",
            """
            { // launchSettings supports comments and trailing commas
              "profiles":{"app":{"applicationUrl":"http://localhost;https://localhost;http://localhost:80;https://[::1]:443;http://*:8203;http://+:8204;;",},},
            }
            """
        );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(0);
        fixture.Console.Output.Count(line => line.Contains(" | ")).Should().Be(5);
        fixture
            .Console.AllText.Should()
            .Contain("10080")
            .And.Contain("10443")
            .And.Contain("18203")
            .And.Contain("18204");
    }

    [Theory]
    [InlineData("10000", 18203)]
    [InlineData("20000", 28203)]
    public async Task RepeatedSourceOccurrencesStayVisibleAndWarnButPublishEachMappingOnlyOnce(
        string offset,
        int hostPort
    )
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        fixture.Root.File(
            "folder/a/Properties/launchSettings.json",
            """{"profiles":{"one":{"applicationUrl":"http://localhost:8203;http://localhost:8203"}}}"""
        );
        fixture.Root.File(
            "folder/b/Properties/launchSettings.json",
            """{"profiles":{"two":{"applicationUrl":"http://localhost:8203"}}}"""
        );

        // Act
        var exit = await fixture.Run("--offset", offset);

        // Assert
        exit.Should().Be(0);
        fixture.Console.Output.Count(line => line.Contains($"| 8203 | {hostPort}")).Should().Be(3);
        fixture.Console.AllText.Should().Contain("Warning").And.Contain("3 occurrences");
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                fixture.Name,
                Arg.Is<IReadOnlyList<ApplicationPortMapping>>(mappings =>
                    mappings.Count == 1 && mappings[0] == new ApplicationPortMapping(8203, hostPort)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ReportsEveryExplicitLaunchProfileUrlWithoutProjectEvaluationOrConfiguration()
    {
        // Arrange
        using var fixture = new PortsCommandFixture();
        fixture.Root.File(
            "folder/api/Properties/launchSettings.json",
            """
            {"profiles":{"http":{"applicationUrl":"http://localhost:8203;https://localhost:8204"},
            "other":{"applicationUrl":"http://0.0.0.0:9000"},"iis":{"commandName":"IISExpress"}}}
            """
        );
        fixture.Root.File(
            "folder/api/api.csproj",
            """<Project><Target Name="NeverEvaluate" BeforeTargets="Build"><Error Text="Project evaluation is forbidden" /></Target></Project>"""
        );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(0);
        fixture
            .Console.AllText.Should()
            .Contain("api")
            .And.Contain("launchSettings.json")
            .And.Contain("8203")
            .And.Contain("18203")
            .And.Contain("8204")
            .And.Contain("18204")
            .And.Contain("19000");
    }
}
