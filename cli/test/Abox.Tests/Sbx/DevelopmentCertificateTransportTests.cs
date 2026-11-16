using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class DevelopmentCertificateTransportTests
{
    [Fact]
    public async Task CertificateInputReachesAControlledChildWithoutExposingEchoedDiagnostics()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var console = new TestConsole();
        var processes = new ProcessRunner(new ProcessOutput(console) { Verbose = true });
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                processes.RunAsync(
                    ControlledCertificateEcho(call.Arg<ProcessRequest>(), root.Path),
                    call.Arg<CancellationToken>()
                )
            );
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        await client.StageDevelopmentCertificateAsync(
            "actual",
            new byte[] { 1, 2 },
            "Grüße 🔒",
            CancellationToken.None
        );

        // Assert
        var bytes = File.ReadAllBytes(Path.Combine(root.Path, "frame.input"));
        bytes.Take(3).Should().Equal((byte)'A', (byte)'B', (byte)'O');
        var frame = Encoding.UTF8.GetString(bytes).Split('\n', 4);
        using var payload = JsonDocument.Parse(frame[3]);
        payload.RootElement.GetProperty("password").GetString().Should().Be("Grüße 🔒");
        payload.RootElement.GetProperty("pfx").GetBytesFromBase64().Should().Equal(1, 2);
        console.AllText.Should().BeEmpty();
    }

    private static ProcessRequest ControlledCertificateEcho(
        ProcessRequest request,
        string directory
    ) =>
        request with
        {
            FileName = OperatingSystem.IsWindows() ? "powershell" : "sh",
            Arguments = OperatingSystem.IsWindows()
                ?
                [
                    "-NoProfile",
                    "-NonInteractive",
                    "-Command",
                    "[Console]::InputEncoding = [Text.UTF8Encoding]::new($false); $text = [Console]::In.ReadToEnd(); [IO.File]::WriteAllText('frame.input', $text, [Text.UTF8Encoding]::new($false)); [Console]::Out.Write($text); [Console]::Error.Write($text)",
                ]
                : ["-c", "cat > frame.input; cat frame.input; cat frame.input >&2"],
            WorkingDirectory = directory,
        };

    [Fact]
    public async Task PrivateBytesAndPasswordTravelOnlyInADigestCheckedLengthFramedSensitiveInput()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        ProcessRequest? captured = null;
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                captured = call.Arg<ProcessRequest>();
                return new ProcessResult(0, "raw synthetic output", "raw synthetic error");
            });
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));
        byte[] pfx = [0, 1, 2, 128, 255];
        const string password = "synthetic-password-🔒";

        // Act
        await client.StageDevelopmentCertificateAsync(
            "actual-sandbox",
            pfx,
            password,
            CancellationToken.None
        );

        // Assert
        captured.Should().NotBeNull();
        captured!
            .Arguments.Take(6)
            .Should()
            .Equal("exec", "-i", "actual-sandbox", "python3", "-I", "-c");
        captured.AttachConsole.Should().BeFalse();
        captured.OutputPolicy.Should().Be(ProcessOutputPolicy.Sensitive);
        captured
            .Arguments.Should()
            .NotContain(argument =>
                argument.Contains(password) || argument.Contains(Convert.ToBase64String(pfx))
            );
        captured.Environment.Should().BeNull();
        var frame = captured.StandardInput!.Split('\n', 4);
        frame[0].Should().Be("ABOX-CERTIFICATE-1");
        var payload = Encoding.UTF8.GetBytes(frame[3]);
        frame[1]
            .Should()
            .Be(payload.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        frame[2].Should().Be(Convert.ToHexStringLower(SHA256.HashData(payload)));
        using var json = JsonDocument.Parse(frame[3]);
        json.RootElement.GetProperty("pfx").GetBytesFromBase64().Should().Equal(pfx);
        json.RootElement.GetProperty("password").GetString().Should().Be(password);
        captured.ToString().Should().NotContain(password);
    }

    [Theory]
    [InlineData("exit", true)]
    [InlineData("exception", true)]
    [InlineData("exit", false)]
    [InlineData("exception", false)]
    [InlineData("cleanup-exception", false)]
    [InlineData("cancel", true)]
    [InlineData("cancel", false)]
    public async Task FailuresSuppressRawDiagnosticsAndAttemptBoundedSensitiveCleanup(
        string failure,
        bool cleanupSucceeds
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        using var scenario = new TransferFailureScenario(failure, cleanupSucceeds ? 0 : 1);
        const string privateText = "synthetic-password 123456789 true";
        var client = new SbxClient(scenario.Runner, AboxPaths.For(root.Path, root.Path));

        // Act
        var action = () =>
            client.StageDevelopmentCertificateAsync(
                "actual",
                new byte[] { 1, 2 },
                "synthetic-password",
                scenario.Cancellation.Token
            );

        // Assert
        var error =
            failure == "cancel"
                ? (Exception)(await action.Should().ThrowAsync<OperationCanceledException>()).Which
                : (await action.Should().ThrowAsync<AboxException>()).Which;
        error.ToString().Should().NotContain(privateText).And.NotContain("synthetic-password");
        error.InnerException.Should().BeNull();
        scenario.Calls.Should().HaveCount(2);
        scenario.CleanupToken.CanBeCanceled.Should().BeTrue();
        scenario.CleanupToken.IsCancellationRequested.Should().BeFalse();
        scenario.CleanupToken.Should().NotBe(scenario.Cancellation.Token);
        var cleanup = scenario.Calls[1];
        cleanup.Arguments.Should().NotContain("-i");
        cleanup.StandardInput.Should().BeEmpty();
        cleanup.OutputPolicy.Should().Be(ProcessOutputPolicy.Sensitive);
        cleanup.Arguments[^1].Should().Be(scenario.Calls[0].Arguments[^1]);
        if (!cleanupSucceeds)
            error.Message.Should().Contain("cleanup is unconfirmed");
        else if (failure != "cancel")
            error
                .Message.Should()
                .Contain("commit was not acknowledged")
                .And.Contain("no rollback is promised");
    }

    private sealed class TransferFailureScenario : IDisposable
    {
        public IProcessRunner Runner { get; } = Substitute.For<IProcessRunner>();
        public List<ProcessRequest> Calls { get; } = [];
        public CancellationTokenSource Cancellation { get; } = new();
        public CancellationToken CleanupToken { get; private set; }

        public TransferFailureScenario(string failure, int cleanupExitCode)
        {
            Runner
                .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    const string privateText = "synthetic-password 123456789 true";
                    var request = call.Arg<ProcessRequest>();
                    Calls.Add(request);
                    if (request.Arguments.Contains("cleanup"))
                    {
                        CleanupToken = call.Arg<CancellationToken>();
                        if (failure == "cleanup-exception")
                            return Task.FromException<ProcessResult>(new IOException(privateText));
                        return Task.FromResult(
                            new ProcessResult(cleanupExitCode, privateText, privateText)
                        );
                    }
                    if (failure == "cancel")
                    {
                        Cancellation.Cancel();
                        return Task.FromException<ProcessResult>(
                            new OperationCanceledException(privateText, Cancellation.Token)
                        );
                    }
                    return failure == "exit"
                        ? Task.FromResult(new ProcessResult(17, privateText, privateText))
                        : Task.FromException<ProcessResult>(new IOException(privateText));
                });
        }

        public void Dispose() => Cancellation.Dispose();
    }

    [Theory]
    [InlineData(0, "valid")]
    [InlineData(1048577, "valid")]
    [InlineData(1, "")]
    [InlineData(1, "nul\0password")]
    [InlineData(1, null)]
    public async Task InvalidPayloadIsRejectedBeforeCallingTheSandbox(int length, string? password)
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "", ""));
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        var action = () =>
            client.StageDevelopmentCertificateAsync(
                "actual",
                new byte[length],
                password!,
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MaximumPayloadAndMultibytePasswordAreAccepted()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "", ""));
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        await client.StageDevelopmentCertificateAsync(
            "actual",
            new byte[1048576],
            new string('é', 1024),
            CancellationToken.None
        );

        // Assert
        await runner.Received(1).RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnresponsiveCleanupExpiresWithoutReplacingTheTransferFailure()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        CancellationToken cleanupToken = default;
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(async call =>
            {
                if (!call.Arg<ProcessRequest>().Arguments.Contains("cleanup"))
                    return new ProcessResult(17, "private child", "private child");
                cleanupToken = call.Arg<CancellationToken>();
                await Task.Delay(Timeout.InfiniteTimeSpan, cleanupToken);
                return new ProcessResult(0, "", "");
            });
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        var action = () =>
            client
                .StageDevelopmentCertificateAsync(
                    "actual",
                    new byte[] { 1 },
                    "password",
                    CancellationToken.None
                )
                .WaitAsync(TimeSpan.FromSeconds(15));

        // Assert
        var error = (await action.Should().ThrowAsync<AboxException>()).Which;
        cleanupToken.IsCancellationRequested.Should().BeTrue();
        error.Message.Should().Contain("cleanup is unconfirmed").And.NotContain("private child");
        error.InnerException.Should().BeNull();
    }

    [Fact]
    public async Task OversizedPasswordIsRejectedBeforeCallingTheSandbox()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "", ""));
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        var action = () =>
            client.StageDevelopmentCertificateAsync(
                "actual",
                new byte[] { 1 },
                new string('é', 1025),
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EmptyMaterialIsRejectedBeforeAnyRuntimeOperation()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        var action = () =>
            client.StageDevelopmentCertificateAsync(
                "actual",
                ReadOnlyMemory<byte>.Empty,
                "synthetic-password",
                CancellationToken.None
            );

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("stage")]
    [InlineData("activate")]
    [InlineData("prepare")]
    [InlineData("status")]
    [InlineData("refresh-bootstrap")]
    public async Task AlreadyCanceledOperationDoesNotStartASandboxEvenForCleanup(string mode)
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var action = () => InvokeCertificateOperationAsync(client, mode, cancellation.Token);

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    private static Task InvokeCertificateOperationAsync(
        SbxClient client,
        string mode,
        CancellationToken token
    ) =>
        mode switch
        {
            "activate" => client.ActivateDevelopmentCertificateAsync(
                "actual",
                new byte[] { 1 },
                "synthetic-password",
                token
            ),
            "prepare" => client.GetDevelopmentCertificatePasswordAsync("actual", token),
            "status" => client.VerifyDevelopmentCertificateActivationAsync("actual", token),
            "refresh-bootstrap" => client.RefreshDevelopmentCertificateBootstrapAsync(
                "actual",
                token
            ),
            _ => client.StageDevelopmentCertificateAsync(
                "actual",
                new byte[] { 1 },
                "synthetic-password",
                token
            ),
        };
}
