using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;
using ShellSandbox = Abox.Tests.UserSecretsTransportTests.ShellSandbox;

namespace Abox.Tests;

public sealed partial class UserSecretsSafetyTests
{
    [Theory]
    [InlineData("../escape", false)]
    [InlineData("../escape", true)]
    [InlineData("CON.txt", false)]
    [InlineData("CON.txt", true)]
    [InlineData("sample.", false)]
    [InlineData("sample.", true)]
    [InlineData("Cape.*", false)]
    [InlineData("Cape.*", true)]
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
}
