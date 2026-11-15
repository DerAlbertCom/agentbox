using System.Text.Json;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;
using ShellSandbox = Abox.Tests.UserSecretsTransportTests.ShellSandbox;

namespace Abox.Tests;

public sealed class UserSecretsSafetyTests
{
    [Theory]
    [InlineData("../escape", false)]
    [InlineData("../escape", true)]
    [InlineData("CON.txt", false)]
    [InlineData("CON.txt", true)]
    [InlineData("sample.", false)]
    [InlineData("sample.", true)]
    public async Task TheSbxAdapterIndependentlyRejectsUnsafeStoreIdsBeforeStartingAProcess(
        string id,
        bool checkOnly
    )
    {
        using var root = new TempDirectory();
        var runner = Substitute.For<IProcessRunner>();
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        var action = async () =>
        {
            if (checkOnly)
                await client.ValidateUserSecretsDestinationAsync(
                    "sandbox",
                    [id],
                    CancellationToken.None
                );
            else
                await client.WriteUserSecretsAsync("sandbox", id, "{}", CancellationToken.None);
        };

        await action.Should().ThrowAsync<AboxException>().WithMessage("*Unsafe user-secret ID*");
        runner.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ReceiverRejectsSymlinkAncestorsAndDestinationsWithoutTouchingTheirTargets(
        bool destinationLink,
        bool checkOnly
    )
    {
        using var sandbox = new ShellSandbox(true);
        using var outside = new TempDirectory();
        var target = outside.File("secrets.json", "synthetic previous contents");
        try
        {
            if (destinationLink)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sandbox.StorePath)!);
                File.CreateSymbolicLink(sandbox.StorePath, target);
            }
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(sandbox.SecretsRoot)!);
                Directory.CreateSymbolicLink(sandbox.SecretsRoot, outside.Path);
            }
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
                    + "This host cannot create the synthetic symlink needed by the receiver test.",
                exception
            );
        }

        var action = () =>
            checkOnly
                ? sandbox.Client.ValidateUserSecretsDestinationAsync(
                    "sandbox",
                    ["synthetic"],
                    CancellationToken.None
                )
                : sandbox.Client.WriteUserSecretsAsync(
                    "sandbox",
                    "synthetic",
                    "{}",
                    CancellationToken.None
                );

        await action.Should().ThrowAsync<AboxException>();
        File.ReadAllText(target).Should().Be("synthetic previous contents");
        Directory.GetFileSystemEntries(outside.Path).Should().ContainSingle();
    }

    [Fact]
    public async Task ReceiverRejectsAHardLinkedDestinationWithoutReplacingEitherName()
    {
        using var sandbox = new ShellSandbox(true);
        Directory.CreateDirectory(Path.GetDirectoryName(sandbox.StorePath)!);
        File.WriteAllText(sandbox.StorePath, "synthetic previous contents");
        var setup = await sandbox.RunShellAsync(
            "ln -- \"$APPDATA/Microsoft/UserSecrets/synthetic/secrets.json\" linked.json"
        );
        setup.ExitCode.Should().Be(0, setup.StandardError);

        var action = () =>
            sandbox.Client.WriteUserSecretsAsync(
                "sandbox",
                "synthetic",
                "{}",
                CancellationToken.None
            );

        await action.Should().ThrowAsync<AboxException>();
        File.ReadAllText(sandbox.StorePath).Should().Be("synthetic previous contents");
        File.ReadAllText(Path.Combine(sandbox.RootPath, "linked.json"))
            .Should()
            .Be("synthetic previous contents");
    }

    [Fact]
    public async Task ReceiverRejectsARuntimeReportingRootBeforeCreatingAnything()
    {
        using var sandbox = new ShellSandbox(true) { BeforeReceiver = "id() { printf '0\\n'; }\n" };

        var action = () =>
            sandbox.Client.WriteUserSecretsAsync(
                "sandbox",
                "synthetic",
                "{}",
                CancellationToken.None
            );

        await action.Should().ThrowAsync<AboxException>();
        Directory.Exists(sandbox.SecretsRoot).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("/synthetic")]
    [InlineData("/synthetic/secrets.json")]
    public async Task ReceiverRejectsReportedWrongOwnershipOfTheRootStoreOrDestination(
        string suffix
    )
    {
        using var sandbox = new ShellSandbox(true)
        {
            BeforeReceiver =
                "stat() { if [ \"$2\" = %u ] && [ \"$4\" = \"$APPDATA/Microsoft/UserSecrets"
                + suffix
                + "\" ]; then printf '0\\n'; else command stat \"$@\"; fi; }\n",
        };
        Directory.CreateDirectory(Path.GetDirectoryName(sandbox.StorePath)!);
        File.WriteAllText(sandbox.StorePath, "synthetic previous contents");

        var action = () =>
            sandbox.Client.WriteUserSecretsAsync(
                "sandbox",
                "synthetic",
                "{}",
                CancellationToken.None
            );

        await action.Should().ThrowAsync<AboxException>();
        File.ReadAllText(sandbox.StorePath).Should().Be("synthetic previous contents");
        Directory.GetFiles(Path.GetDirectoryName(sandbox.StorePath)!).Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, "Server=localhost", false)]
    [InlineData(true, "Server=localhost", false)]
    [InlineData(true, "Server=PROD.EXAMPLE", true)]
    public async Task SharedReportsRecordAnActualSelectedOriginalValueOverride(
        bool force,
        string value,
        bool expectedOverride
    )
    {
        using var root = new TempDirectory();
        root.File("abox.json", """{"userSecrets":["sample"]}""");
        root.File(
            ".microsoft/usersecrets/sample/secrets.json",
            JsonSerializer.Serialize(new { Db = value })
        );
        var copy = new UserSecretsCopy(
            new AboxConfig { UserSecretsDenyList = ["prod.example"] },
            new UserSecretsHost(null, root.Path),
            Substitute.For<ISbxClient>()
        );

        var prepared = await copy.PrepareAsync(
            await FolderConfig.LoadAsync(root.Path, CancellationToken.None),
            force,
            CancellationToken.None
        );
        var report = await copy.CopyAsync(prepared, "sandbox", CancellationToken.None);

        prepared.SafetyOverride.Should().Be(expectedOverride);
        report.SafetyOverride.Should().Be(expectedOverride);
    }

    [Fact]
    public async Task SensitiveAttachedConsoleIsRejectedBeforeTheChildStarts()
    {
        using var root = new TempDirectory();
        var console = new TestConsole();
        var runner = new ProcessRunner(new ProcessOutput(console) { Verbose = true });
        var request = new ProcessRequest(
            OperatingSystem.IsWindows() ? "cmd" : "sh",
            OperatingSystem.IsWindows()
                ? ["/c", "echo started > started.txt"]
                : ["-c", "touch started.txt"],
            WorkingDirectory: root.Path,
            AttachConsole: true,
            OutputPolicy: ProcessOutputPolicy.Sensitive
        );

        var action = () => runner.RunAsync(request, CancellationToken.None);

        await action.Should().ThrowAsync<AboxException>().WithMessage("*suppressed*");
        File.Exists(Path.Combine(root.Path, "started.txt")).Should().BeFalse();
        console.AllText.Should().BeEmpty();
    }

    [Fact]
    public async Task SensitiveStartFailureDoesNotExposeRawExceptionDetailsEvenInVerboseMode()
    {
        using var root = new TempDirectory();
        var console = new TestConsole();
        var runner = new ProcessRunner(new ProcessOutput(console) { Verbose = true });
        var request = new ProcessRequest(
            Path.Combine(root.Path, "synthetic-private-missing-executable"),
            ["123456789 true"],
            StandardInput: "synthetic-password",
            OutputPolicy: ProcessOutputPolicy.Sensitive
        );

        var action = () => runner.RunAsync(request, CancellationToken.None);

        var error = await action.Should().ThrowAsync<AboxException>();
        error
            .Which.ToString()
            .Should()
            .Contain("suppressed")
            .And.NotContain("synthetic-private")
            .And.NotContain(root.Path)
            .And.NotContain("synthetic-password")
            .And.NotContain("123456789 true");
        console.AllText.Should().BeEmpty();
    }

    [Theory]
    [InlineData("success", 0)]
    [InlineData("preflight", 1)]
    [InlineData("lookup", 1)]
    [InlineData("destination", 1)]
    [InlineData("transfer", 1)]
    [InlineData("cancellation", 130)]
    public async Task AnActualOverrideIsReportedOnlyAfterSuccessfulCopy(string result, int exitCode)
    {
        using var fixture = new Fixture();
        fixture.Store("""{"Db":"Server=PROD.EXAMPLE;Password=synthetic"}""");
        if (result == "preflight")
            fixture
                .Sbx.GetVersionAsync(Arg.Any<CancellationToken>())
                .Returns(new Version(0, 50, 0));
        if (result == "lookup")
            fixture
                .Sbx.FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((SandboxInfo?)null);
        if (result == "destination")
            fixture
                .Sbx.ValidateUserSecretsDestinationAsync(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Task.FromException(new AboxException("synthetic failure")));
        var overrideReportedDuringCopy = false;
        fixture
            .Sbx.WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                overrideReportedDuringCopy = fixture.Console.AllText.Contains("Safety override");
                return result switch
                {
                    "transfer" => Task.FromException(new AboxException("synthetic failure")),
                    "cancellation" => Task.FromException(new OperationCanceledException()),
                    _ => Task.CompletedTask,
                };
            });

        (await fixture.Run("--force")).Should().Be(exitCode);

        overrideReportedDuringCopy.Should().BeFalse();
        if (result == "success")
            fixture.Console.AllText.Should().Contain("Safety override").And.Contain("matched");
        else
            fixture.Console.AllText.Should().NotContain("Safety override");
        fixture
            .Console.AllText.Should()
            .NotContain("PROD.EXAMPLE")
            .And.NotContain("Password=synthetic");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForceWithoutADenyMatchDoesNotClaimThatAnOverrideWasUsed(bool emptySelection)
    {
        using var fixture = new Fixture();
        if (emptySelection)
            fixture.Root.File("folder/abox.json", """{"userSecrets":[]}""");
        else
            fixture.Store("""{"Db":"Server=localhost"}""");

        (await fixture.Run("--force")).Should().Be(0);

        fixture.Console.AllText.Should().NotContain("Safety override");
    }

    [Fact]
    public async Task DestinationChecksCannotForwardTheHostStandardInput()
    {
        using var root = new TempDirectory();
        ProcessRequest? captured = null;
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<ProcessRequest>();
                return new ProcessResult(0, "", "");
            });
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        await client.ValidateUserSecretsDestinationAsync(
            "sandbox",
            ["sample"],
            CancellationToken.None
        );

        captured.Should().NotBeNull();
        captured!.Arguments.Should().NotContain("-i").And.NotContain("-t");
        captured
            .StandardInput.Should()
            .BeEmpty(
                "an empty redirected pipe isolates even the local sbx process from host stdin"
            );
        captured.AttachConsole.Should().BeFalse();
        captured.OutputPolicy.Should().Be(ProcessOutputPolicy.Sensitive);
    }

    [Theory]
    [InlineData(false, "{ /* Server=prod.example;Password=secret */ \"Db\":\"Server=localhost\" }")]
    [InlineData(true, "{ /* Server=prod.example;Password=secret */ \"Db\":\"Server=localhost\" }")]
    [InlineData(
        false,
        "{\"Nested\":{/* prod.example */\"Items\":[\"Server=localhost\",/* prod.example */],},// prod.example\n}"
    )]
    [InlineData(
        true,
        "{\"Nested\":{/* prod.example */\"Items\":[\"Server=localhost\",/* prod.example */],},// prod.example\n}"
    )]
    public async Task OnlyParsedJsonDataIsTransferredNeverCommentsOrTrailingCommas(
        bool filtered,
        string source
    )
    {
        using var fixture = new Fixture();
        if (filtered)
            fixture.Root.File(
                "folder/abox.json",
                """{"userSecrets":[{"id":"sample","keys":["Db","Nested:*"]}]}"""
            );
        fixture.Store(source);
        string? transferred = null;
        fixture
            .Sbx.WriteUserSecretsAsync(
                Arg.Any<string>(),
                "sample",
                Arg.Do<string>(json => transferred = json),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        (await fixture.Run()).Should().Be(0);

        transferred.Should().NotBeNull();
        transferred.Should().NotContain("prod.example").And.NotContain("/*").And.NotContain("//");
        using var document = JsonDocument.Parse(transferred!);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        transferred.Should().Contain("Server=localhost");
        File.ReadAllText(fixture.SourcePath).Should().Be(source);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly UserSecretsCliHarness cli = new(
            new AboxConfig { UserSecretsDenyList = ["prod.example"] }
        );
        public TempDirectory Root => cli.Root;
        public TestConsole Console => cli.Console;
        public ISbxClient Sbx => cli.Sbx;
        public string SourcePath =>
            Path.Combine(Root.Path, "appdata/Microsoft/UserSecrets/sample/secrets.json");

        public Fixture()
        {
            cli.Configure("""{"userSecrets":["sample"]}""");
        }

        public void Store(string json) => cli.Store("sample", json);

        public Task<int> Run(params string[] args) => cli.Run(args);

        public void Dispose() => cli.Dispose();
    }
}
