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
        // Arrange
        using var sandbox = new ShellSandbox(true) { BeforeReceiver = environment };

        // Act
        var action = () =>
            sandbox.Client.WriteUserSecretsAsync(
                "sandbox",
                "synthetic",
                "{}",
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        Directory.Exists(sandbox.SecretsRoot).Should().BeFalse();
    }

    [Fact]
    public async Task ALaterInvalidDestinationIsDetectedBeforeAnyDirectoriesAreCreated()
    {
        // Arrange
        using var sandbox = new ShellSandbox(true);
        Directory.CreateDirectory(sandbox.StorePath);

        // Act
        var action = () =>
            sandbox.Client.ValidateUserSecretsDestinationAsync(
                "sandbox",
                ["first", "synthetic"],
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        Directory.Exists(Path.Combine(sandbox.SecretsRoot, "first")).Should().BeFalse();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReceiverResolvesEffectiveEnvironmentAndReplacesTheWholeStore(bool appData)
    {
        // Arrange
        using var sandbox = new ShellSandbox(appData);

        // Act
        await sandbox.Client.ValidateUserSecretsDestinationAsync(
            "sandbox",
            ["synthetic"],
            CancellationToken.None
        );
        // Assert
        Directory
            .Exists(sandbox.SecretsRoot)
            .Should()
            .BeFalse("destination preflight must not create anything");
        // Arrange
        const string original = "{\"Password\":\"Grüße 🔒\",\"Removed\":\"old\"}";

        // Act
        await sandbox.Client.WriteUserSecretsAsync(
            "sandbox",
            "synthetic",
            original,
            CancellationToken.None
        );
        // Assert
        File.ReadAllBytes(sandbox.StorePath).Should().Equal(Encoding.UTF8.GetBytes(original));

        // Act
        await sandbox.Client.WriteUserSecretsAsync(
            "sandbox",
            "synthetic",
            "{}",
            CancellationToken.None
        );

        // Assert
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
        // Arrange
        using var sandbox = new ShellSandbox(true);
        await sandbox.Client.WriteUserSecretsAsync(
            "sandbox",
            "synthetic",
            "{\"Previous\":true}",
            CancellationToken.None
        );
        sandbox.Frame = frame => damage == "short" ? frame[..^3] : frame[..^1] + "x";

        // Act
        var action = () =>
            sandbox.Client.WriteUserSecretsAsync(
                "sandbox",
                "synthetic",
                "{\"Replacement\":123456789}",
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        File.ReadAllText(sandbox.StorePath).Should().Be("{\"Previous\":true}");
        Directory.GetFiles(Path.GetDirectoryName(sandbox.StorePath)!).Should().ContainSingle();
    }

    [Fact]
    public async Task CancellationWhileInputIsBlockedDoesNotReplaceThePreviousFile()
    {
        // Arrange
        using var sandbox = new ShellSandbox(true);
        await sandbox.Client.WriteUserSecretsAsync(
            "sandbox",
            "synthetic",
            "{}",
            CancellationToken.None
        );
        sandbox.BeforeReceiver = "sleep 30\n";
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));

        // Act
        var action = () =>
            sandbox.Client.WriteUserSecretsAsync(
                "sandbox",
                "synthetic",
                "{\"Synthetic\":\"" + new string('x', 8 * 1024 * 1024) + "\"}",
                cancellation.Token
            );

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        File.ReadAllText(sandbox.StorePath).Should().Be("{}");
    }

    [Fact]
    public async Task NonregularDestinationIsRejectedWithoutReplacingIt()
    {
        // Arrange
        using var sandbox = new ShellSandbox(true);
        Directory.CreateDirectory(sandbox.StorePath);

        // Act
        var action = () =>
            sandbox.Client.WriteUserSecretsAsync(
                "sandbox",
                "synthetic",
                "{}",
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        Directory.Exists(sandbox.StorePath).Should().BeTrue();
    }

    [Theory]
    [InlineData("exit")]
    [InlineData("exception")]
    public async Task RawFailuresIncludingScalarFragmentsNeverEscapeTheSbxAdapter(string failure)
    {
        // Arrange
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

        // Act
        var action = () =>
            client.WriteUserSecretsAsync("sandbox", "synthetic", "{}", CancellationToken.None);

        // Assert
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
        // Arrange
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

        // Act
        await client.WriteUserSecretsAsync("sandbox", "synthetic", payload, CancellationToken.None);

        // Assert
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
    internal sealed class ShellSandbox : IProcessRunner, IDisposable
    {
        private static readonly Lazy<string?> Shell = new(FindShell);
        private readonly TempDirectory root;
        private readonly bool appData;
        public SbxClient Client { get; }
        public string RootPath => root.Path;
        public string SecretsRoot { get; }
        public string StorePath => Path.Combine(SecretsRoot, "synthetic", "secrets.json");
        public Func<string, string>? Frame { get; set; }
        public string BeforeReceiver { get; set; } = "";

        public ShellSandbox(bool appData)
        {
            if (Shell.Value is null)
                throw new InvalidOperationException(
                    Xunit.v3.DynamicSkipToken.Value
                        + "Receiver tests require a non-root POSIX shell with GNU coreutils (Git Bash on Windows, discovered from git on PATH)."
                );
            root = new TempDirectory();
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
            var start = request.Arguments.ToList().IndexOf("-c");
            var script =
                BeforeReceiver + (appData ? "" : "unset APPDATA\n") + request.Arguments[start + 1];
            return new ProcessRunner().RunAsync(
                new ProcessRequest(
                    Shell.Value!,
                    ["-c", script, .. request.Arguments.Skip(start + 2)],
                    Environment: ShellEnvironment,
                    StandardInput: request.StandardInput is { } frame
                        ? Frame?.Invoke(frame) ?? frame
                        : null,
                    OutputPolicy: request.OutputPolicy
                ),
                cancellationToken
            );
        }

        public Task<ProcessResult> RunShellAsync(string script) =>
            new ProcessRunner().RunAsync(
                new ProcessRequest(
                    Shell.Value!,
                    ["-c", script],
                    WorkingDirectory: root.Path,
                    Environment: ShellEnvironment,
                    StandardInput: ""
                ),
                CancellationToken.None
            );

        private Dictionary<string, string> ShellEnvironment =>
            new()
            {
                ["HOME"] = UnixPath(Path.Combine(root.Path, "home")),
                ["APPDATA"] = UnixPath(Path.Combine(root.Path, "appdata")),
            };

        private static string? FindShell()
        {
            IEnumerable<string> candidates = ["sh"];
            if (OperatingSystem.IsWindows())
                candidates = (Environment.GetEnvironmentVariable("PATH") ?? "")
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Where(directory => File.Exists(Path.Combine(directory, "git.exe")))
                    .SelectMany(directory =>
                        new[]
                        {
                            directory,
                            Path.Combine(directory, ".."),
                            Path.Combine(directory, "../.."),
                        }
                    )
                    .SelectMany(directory =>
                        new[]
                        {
                            Path.Combine(directory, "bin/bash.exe"),
                            Path.Combine(directory, "usr/bin/bash.exe"),
                        }
                    )
                    .Select(Path.GetFullPath)
                    .Where(File.Exists)
                    .Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    var result = new ProcessRunner()
                        .RunAsync(
                            new ProcessRequest(
                                candidate,
                                [
                                    "-c",
                                    "command -v stat mktemp sha256sum mv wc tr cut grep id chmod cat mkdir rm >/dev/null && [ \"$(id -u)\" -ne 0 ] && stat -c %u . >/dev/null 2>&1",
                                ],
                                StandardInput: ""
                            ),
                            timeout.Token
                        )
                        .GetAwaiter()
                        .GetResult();
                    if (result.Succeeded)
                        return candidate;
                }
                catch (Exception exception)
                    when (exception is AboxException or OperationCanceledException)
                {
                    // Report an explicit test skip if no compatible application-user shell is available.
                }
            return null;
        }

        private static string UnixPath(string path) =>
            OperatingSystem.IsWindows()
                ? "/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/')
                : path;

        public void Dispose() => root.Dispose();
    }
}
