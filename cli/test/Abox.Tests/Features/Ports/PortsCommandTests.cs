using Abox.Cli;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed partial class PortsCommandTests
{
    [Fact]
    public async Task SelectedRootMayBeLinkedButKeepsItsLexicalSandboxIdentity()
    {
        // Arrange
        using var fixture = new Fixture();
        using var links = new DirectoryLinks();
        fixture.Root.File(
            "folder/Properties/launchSettings.json",
            """{"profiles":{"app":{"applicationUrl":"http://localhost:55535"}}}"""
        );
        var link = Path.Combine(fixture.Root.Path, "selected-root");
        await links.Add(link, fixture.Location);
        fixture
            .Sbx.FindSandboxAsync(SandboxName.For(link), Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo("linked-root-identity", "running", null));

        // Act
        var exit = await fixture.Run("--location", link);

        // Assert
        exit.Should().Be(0);
        fixture.Console.AllText.Should().Contain("| 55535 | 65535");
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                "linked-root-identity",
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task MatchingFileLinksAreRejectedWithoutReadingTheTarget()
    {
        // Arrange
        using var fixture = new Fixture();
        var properties = fixture.Root.Directory_("folder/Properties");
        var target = fixture.Root.File("synthetic-target.json", "must-never-be-read");
        var path = Path.Combine(properties, "launchSettings.json");
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
            .Contain("launchSettings.json")
            .And.NotContain("must-never-be-read");
        await fixture.NoPublishing();
    }

    [Fact]
    public async Task InvalidUtf8IsAnActionableDiscoveryFailure()
    {
        // Arrange
        using var fixture = new Fixture();
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
        using var fixture = new Fixture();
        fixture.Root.File(
            "folder/Properties/launchSettings.json",
            """{"profiles":{"app":{"applicationUrl":"http://localhost:8203"}}}"""
        );
        fixture
            .Sbx.FindSandboxAsync(fixture.Name, Arg.Any<CancellationToken>())
            .Returns(legacy ? null : new SandboxInfo("selected-current-identity", "running", null));
        fixture
            .Sbx.FindSandboxAsync(
                Arg.Is<string>(name => name.Contains("-ai-sbx-")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new SandboxInfo("selected-legacy-identity", "stopped", null));

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(0);
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                legacy ? "selected-legacy-identity" : "selected-current-identity",
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            );
        if (!legacy)
            await fixture
                .Sbx.DidNotReceive()
                .FindSandboxAsync(
                    Arg.Is<string>(name => name.Contains("-ai-sbx-")),
                    Arg.Any<CancellationToken>()
                );
    }

    [Fact]
    public async Task ExplicitLocationOverridesDefaultWithoutRequiringFolderConfigTeamSourcesOrBuilds()
    {
        // Arrange
        using var fixture = new Fixture();
        var other = fixture.Root.Directory_("explicit");
        fixture.Root.File(
            "explicit/Properties/launchSettings.json",
            """{"profiles":{"app":{"applicationUrl":"http://localhost:8203"}}}"""
        );
        fixture
            .Sbx.FindSandboxAsync(SandboxName.For(other), Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo("explicit-identity", "running", null));

        // Act
        var exit = await fixture.Run("--location", other);

        // Assert
        exit.Should().Be(0);
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                "explicit-identity",
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Sbx.DidNotReceive()
            .FindSandboxAsync(fixture.Name, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnconfiguredLocationUsesCurrentFolderAndMissingSandboxGivesCreateHintWithoutCreation()
    {
        // Arrange
        using var fixture = new Fixture(new AboxConfig());

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(1);
        await fixture
            .Sbx.Received(1)
            .FindSandboxAsync(
                SandboxName.For(Environment.CurrentDirectory),
                Arg.Any<CancellationToken>()
            );
        fixture
            .Console.AllText.Should()
            .Contain("abox create --location")
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
        using var fixture = new Fixture();
        fixture
            .Sbx.FindSandboxAsync(fixture.Name, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<SandboxInfo?>(
                    cancel
                        ? new OperationCanceledException()
                        : new AboxException("runtime discovery unavailable")
                )
            );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(expected);
        await fixture
            .Sbx.DidNotReceive()
            .FindSandboxAsync(
                Arg.Is<string>(name => name.Contains("-ai-sbx-")),
                Arg.Any<CancellationToken>()
            );
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
        using var fixture = new Fixture();
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
        using var fixture = new Fixture();
        fixture.Sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 50, 0));

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(1);
        await fixture.NoPublishing();
        await fixture
            .Sbx.DidNotReceive()
            .FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
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
        using var fixture = new Fixture();
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
                        : new AboxException("host port 18203 is already in use")
                )
            );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(expectedExit);
        fixture
            .Console.AllText.Should()
            .Contain("retry")
            .And.Contain("may remain")
            .And.NotContain("Application ports published");
        if (!cancel)
            fixture.Console.AllText.Should().Contain("18203").And.Contain("already in use");
    }

    [Fact]
    public async Task DiscoveryStaysBelowLocationAndSkipsArtifactsDirectoryLinksAndCycles()
    {
        // Arrange
        using var fixture = new Fixture();
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
        using var fixture = new Fixture();
        if (limit == "oversized")
            fixture.Root.File(
                "folder/Properties/launchSettings.json",
                new string(' ', 1024 * 1024 + 1)
            );
        else
            fixture.Root.Directory_("folder/" + string.Join('/', Enumerable.Repeat("d", 65)));

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
        using var fixture = new Fixture();
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
        using var fixture = new Fixture();
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

    [Fact]
    public async Task RepeatedSourceOccurrencesStayVisibleAndWarnButPublishEachMappingOnlyOnce()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Root.File(
            "folder/a/Properties/launchSettings.json",
            """{"profiles":{"one":{"applicationUrl":"http://localhost:8203;http://localhost:8203"}}}"""
        );
        fixture.Root.File(
            "folder/b/Properties/launchSettings.json",
            """{"profiles":{"two":{"applicationUrl":"http://localhost:8203"}}}"""
        );

        // Act
        var exit = await fixture.Run();

        // Assert
        exit.Should().Be(0);
        fixture.Console.Output.Count(line => line.Contains("| 8203 | 18203")).Should().Be(3);
        fixture.Console.AllText.Should().Contain("Warning").And.Contain("3 occurrences");
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                fixture.Name,
                Arg.Is<IReadOnlyList<ApplicationPortMapping>>(mappings =>
                    mappings.Count == 1 && mappings[0] == new ApplicationPortMapping(8203, 18203)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ReportsEveryExplicitLaunchProfileUrlWithoutProjectEvaluationOrConfiguration()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Root.File(
            "folder/api/Properties/launchSettings.json",
            """
            {"profiles":{"http":{"applicationUrl":"http://localhost:8203;https://localhost:8204"},
            "other":{"applicationUrl":"http://0.0.0.0:9000"},"iis":{"commandName":"IISExpress"}}}
            """
        );
        fixture.Root.File("folder/api/api.csproj", "not evaluated or parsed");

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

    private sealed class Fixture : IDisposable
    {
        public TempDirectory Root { get; } = new();
        public TestConsole Console { get; } = new();
        public ISbxClient Sbx { get; } = Substitute.For<ISbxClient>();
        public string Location { get; }
        public string Name => SandboxName.For(Location);
        private readonly ServiceProvider provider;

        public Fixture(AboxConfig? config = null)
        {
            Location = Root.Directory_("folder");
            Sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 51, 0));
            Sbx.DiagnoseAsync(Arg.Any<CancellationToken>()).Returns(new DiagnoseReport([]));
            Sbx.FindSandboxAsync(Name, Arg.Any<CancellationToken>())
                .Returns(new SandboxInfo(Name, "running", null));
            var programs = Substitute.For<IHostPrograms>();
            programs.Exists(Arg.Any<string>()).Returns(true);
            provider = new ServiceCollection()
                .AddAbox(Console, new TrailingArguments())
                .AddSingleton(config ?? new AboxConfig { DefaultLocation = Location })
                .AddSingleton(AboxPaths.For(Root.Path, Root.Path))
                .AddSingleton(Sbx)
                .AddSingleton(programs)
                .BuildServiceProvider();
        }

        public Task<int> Run(params string[] args) =>
            provider.GetRequiredService<AboxRootCommand>().InvokeAsync(["ports", .. args]);

        public async Task NoPublishing() =>
            await Sbx.DidNotReceive()
                .EnsureApplicationPortsAsync(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                    Arg.Any<CancellationToken>()
                );

        public void Dispose()
        {
            provider.Dispose();
            Root.Dispose();
        }
    }
}
