using System.Security.Cryptography;
using System.Text;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsTransportTests
{
    [Theory]
    [InlineData("export APPDATA=\n")]
    [InlineData("export APPDATA=relative\n")]
    public async Task InvalidEffectiveAppDataCannotFallBackToHome(string environment)
    {
        using var sandbox = new ShellSandbox(true) { BeforeReceiver = environment };

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

    [Fact]
    public async Task ALaterInvalidDestinationIsDetectedBeforeAnyDirectoriesAreCreated()
    {
        using var sandbox = new ShellSandbox(true);
        Directory.CreateDirectory(sandbox.StorePath);

        var action = () =>
            sandbox.Client.ValidateUserSecretsDestinationAsync(
                "sandbox",
                ["first", "synthetic"],
                CancellationToken.None
            );

        await action.Should().ThrowAsync<AboxException>();
        Directory.Exists(Path.Combine(sandbox.SecretsRoot, "first")).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReceiverResolvesEffectiveEnvironmentAndReplacesTheWholeStore(bool appData)
    {
        using var sandbox = new ShellSandbox(appData);
        await sandbox.Client.ValidateUserSecretsDestinationAsync(
            "sandbox",
            ["synthetic"],
            CancellationToken.None
        );
        Directory
            .Exists(sandbox.SecretsRoot)
            .Should()
            .BeFalse("destination preflight must not create anything");
        const string original = "{\"Password\":\"Grüße 🔒\",\"Removed\":\"old\"}";

        await sandbox.Client.WriteUserSecretsAsync(
            "sandbox",
            "synthetic",
            original,
            CancellationToken.None
        );
        File.ReadAllBytes(sandbox.StorePath).Should().Equal(Encoding.UTF8.GetBytes(original));
        await sandbox.Client.WriteUserSecretsAsync(
            "sandbox",
            "synthetic",
            "{}",
            CancellationToken.None
        );

        File.ReadAllText(sandbox.StorePath).Should().Be("{}");
        Directory.GetFiles(Path.GetDirectoryName(sandbox.StorePath)!).Should().ContainSingle();
        if (!OperatingSystem.IsWindows())
        {
            File.GetUnixFileMode(sandbox.StorePath)
                .Should()
                .Be(UnixFileMode.UserRead | UnixFileMode.UserWrite);
            File.GetUnixFileMode(Path.GetDirectoryName(sandbox.StorePath)!)
                .Should()
                .Be(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Theory]
    [InlineData("short")]
    [InlineData("corrupt")]
    public async Task ShortOrCorruptInputCannotCommitEvenWhenStdinCloses(string damage)
    {
        using var sandbox = new ShellSandbox(true);
        await sandbox.Client.WriteUserSecretsAsync(
            "sandbox",
            "synthetic",
            "{\"Previous\":true}",
            CancellationToken.None
        );
        sandbox.Frame = frame => damage == "short" ? frame[..^3] : frame[..^1] + "x";

        var action = () =>
            sandbox.Client.WriteUserSecretsAsync(
                "sandbox",
                "synthetic",
                "{\"Replacement\":123456789}",
                CancellationToken.None
            );

        await action.Should().ThrowAsync<AboxException>();
        File.ReadAllText(sandbox.StorePath).Should().Be("{\"Previous\":true}");
        Directory.GetFiles(Path.GetDirectoryName(sandbox.StorePath)!).Should().ContainSingle();
    }

    [Fact]
    public async Task CancellationWhileInputIsBlockedDoesNotReplaceThePreviousFile()
    {
        using var sandbox = new ShellSandbox(true);
        await sandbox.Client.WriteUserSecretsAsync(
            "sandbox",
            "synthetic",
            "{}",
            CancellationToken.None
        );
        sandbox.BeforeReceiver = "sleep 30\n";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        var action = () =>
            sandbox.Client.WriteUserSecretsAsync(
                "sandbox",
                "synthetic",
                "{\"Synthetic\":\"" + new string('x', 8 * 1024 * 1024) + "\"}",
                cancellation.Token
            );

        await action.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(sandbox.StorePath).Should().Be("{}");
    }

    [Fact]
    public async Task NonregularDestinationIsRejectedWithoutReplacingIt()
    {
        using var sandbox = new ShellSandbox(true);
        Directory.CreateDirectory(sandbox.StorePath);

        var action = () =>
            sandbox.Client.WriteUserSecretsAsync(
                "sandbox",
                "synthetic",
                "{}",
                CancellationToken.None
            );

        await action.Should().ThrowAsync<AboxException>();
        Directory.Exists(sandbox.StorePath).Should().BeTrue();
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("exception")]
    public async Task RawFailuresIncludingScalarFragmentsNeverEscapeTheSbxAdapter(string failure)
    {
        using var root = new TempDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns<Task<ProcessResult>>(_ =>
                failure == "exit"
                    ? Task.FromResult(new ProcessResult(9, "123456789 true", "synthetic-password"))
                    : Task.FromException<ProcessResult>(
                        new AboxException("synthetic-password 123456789 true")
                    )
            );
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        var action = () =>
            client.WriteUserSecretsAsync("sandbox", "synthetic", "{}", CancellationToken.None);

        var error = await action.Should().ThrowAsync<AboxException>();
        error
            .Which.ToString()
            .Should()
            .NotContain("synthetic-password")
            .And.NotContain("123456789 true");
    }

    [Fact]
    public async Task PayloadAndFrameUseOnlySensitiveNoninteractiveStdinWithUtf8ByteLength()
    {
        using var root = new TempDirectory();
        const string payload = "{\"Password\":\"Grüße 🔒\"}";
        ProcessRequest? captured = null;
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<ProcessRequest>();
                return new ProcessResult(0, "must not escape", "must not escape");
            });
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        await client.WriteUserSecretsAsync("sandbox", "synthetic", payload, CancellationToken.None);

        captured.Should().NotBeNull();
        captured!.FileName.Should().Be("sbx");
        captured.Arguments.Take(2).Should().Equal("exec", "-i");
        captured.Arguments.Should().NotContain("-t").And.NotContain("-u");
        string.Join(" ", captured.Arguments)
            .Should()
            .NotContain("Grüße")
            .And.NotContain("Password");
        captured.AttachConsole.Should().BeFalse();
        captured.OutputPolicy.Should().Be(ProcessOutputPolicy.Sensitive);
        captured
            .StandardInput.Should()
            .Be(
                $"{Encoding.UTF8.GetByteCount(payload)}\n{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(payload)))}\n{payload}"
            );
        captured.ToString().Should().NotContain("Grüße");
    }

    // The approved process seam executes the actual captured receiver against a
    // disposable application-user filesystem, not a second implementation of it.
    private sealed class ShellSandbox : IProcessRunner, IDisposable
    {
        private readonly TempDirectory root = new();
        private readonly bool appData;
        public SbxClient Client { get; }
        public string SecretsRoot { get; }
        public string StorePath => Path.Combine(SecretsRoot, "synthetic", "secrets.json");
        public Func<string, string>? Frame { get; set; }
        public string BeforeReceiver { get; set; } = "";

        public ShellSandbox(bool appData)
        {
            this.appData = appData;
            root.Directory_("appdata");
            root.Directory_("home");
            SecretsRoot = Path.Combine(
                root.Path,
                appData ? "appdata/Microsoft/UserSecrets" : "home/.microsoft/usersecrets"
            );
            Client = new SbxClient(this, AboxPaths.For(root.Path, root.Path));
        }

        public Task<ProcessResult> RunAsync(
            ProcessRequest request,
            CancellationToken cancellationToken
        )
        {
            var shell = OperatingSystem.IsWindows() ? @"C:\Program Files\Git\bin\bash.exe" : "sh";
            var start = request.Arguments.ToList().IndexOf("-c");
            var script =
                BeforeReceiver + (appData ? "" : "unset APPDATA\n") + request.Arguments[start + 1];
            var environment = new Dictionary<string, string>
            {
                ["HOME"] = UnixPath(Path.Combine(root.Path, "home")),
                ["APPDATA"] = UnixPath(Path.Combine(root.Path, "appdata")),
            };
            return new ProcessRunner().RunAsync(
                new ProcessRequest(
                    shell,
                    ["-c", script, .. request.Arguments.Skip(start + 2)],
                    Environment: environment,
                    StandardInput: request.StandardInput is { } frame
                        ? Frame?.Invoke(frame) ?? frame
                        : null,
                    OutputPolicy: request.OutputPolicy
                ),
                cancellationToken
            );
        }

        private static string UnixPath(string path) =>
            OperatingSystem.IsWindows()
                ? "/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/')
                : path;

        public void Dispose() => root.Dispose();
    }
}
