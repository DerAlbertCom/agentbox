using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class DevelopmentCertificateActivationTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task SharedExecBootstrapPreservesArgvTtyStreamsWorkdirAndExitCode(
        bool input,
        bool tty
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        ProcessRequest? observed = null;
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                observed = call.Arg<ProcessRequest>();
                return new ProcessResult(37, "", "");
            });
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));
        string[] command = ["sh", "-c", "printf '%s' \"$1\"", "_", "a b;$(literal)\n\"'🔒"];

        // Act
        var exit = await client.ExecAsync(
            new SbxExecRequest(
                "actual",
                command,
                AllocateTty: tty,
                KeepStandardInputOpen: input,
                WorkingDirectory: "/workspace/a b"
            ),
            CancellationToken.None
        );

        // Assert
        exit.Should().Be(37);
        observed!.AttachConsole.Should().BeTrue();
        observed.StandardInput.Should().BeNull();
        observed.Arguments.Contains("-i").Should().Be(input);
        observed.Arguments.Contains("-t").Should().Be(tty);
        observed.Arguments.Should().Contain("/workspace/a b");
        observed.Arguments.Sum(argument => argument.Length).Should().BeLessThan(4000);
        observed.Arguments.TakeLast(command.Length).Should().Equal(command);
        observed.Environment.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ActivationTrustsOnlyTheExactPublicLeafAsRootBeforeNonrootPrivateActivation(
        bool trusted
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1)
        );
        var password = new string('a', 64);
        var pfx = certificate.Export(X509ContentType.Pfx, password);
        var calls = new List<ProcessRequest>();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var process = call.Arg<ProcessRequest>();
                calls.Add(process);
                return new ProcessResult(
                    process.Arguments.Contains("root") && !trusted ? 1 : 0,
                    process.Arguments.Contains("id") ? "1000\n" : "",
                    "synthetic private child output"
                );
            });
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        var action = () =>
            client.ActivateDevelopmentCertificateAsync(
                "actual",
                pfx,
                password,
                CancellationToken.None
            );

        // Assert
        if (trusted)
            await action();
        else
            (await action.Should().ThrowAsync<AboxException>())
                .Which.Message.Should()
                .Contain("sandbox trust")
                .And.NotContain("synthetic private");
        var rootCall = calls.Single(process => process.Arguments.Contains("root"));
        rootCall.StandardInput.Should().Be(certificate.ExportCertificatePem());
        rootCall.StandardInput.Should().NotContain(password).And.NotContain("PRIVATE KEY");
        rootCall.Arguments.Should().Contain("1000");
        rootCall.OutputPolicy.Should().Be(ProcessOutputPolicy.Sensitive);
        calls.Any(process => process.Arguments.Contains("activate")).Should().Be(trusted);
        if (trusted)
            calls
                .IndexOf(rootCall)
                .Should()
                .BeLessThan(calls.FindIndex(process => process.Arguments.Contains("activate")));
    }

    [Fact]
    public async Task ExistingParentFailureIsActionableAndNeverSilentlyRestartsIt()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(42, "private", "private"));
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        var action = () =>
            client.VerifyDevelopmentCertificateActivationAsync("actual", CancellationToken.None);

        // Assert
        var failure = (await action.Should().ThrowAsync<AboxException>()).Which;
        failure
            .Message.Should()
            .Contain("fresh")
            .And.Contain("hc server restart --fresh")
            .And.NotContain("private");
        await runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.OutputPolicy == ProcessOutputPolicy.Sensitive
                    && !request.Arguments.Contains("restart")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(43, "/proc")]
    [InlineData(44, "NODE_EXTRA_CA_CERTS")]
    [InlineData(75, "busy")]
    [InlineData(17, "Cannot verify protected HTTPS launch configuration")]
    public async Task ReadinessExplainsProcessVisibilityAndExtraCaFailuresWithoutClaimingCompletion(
        int code,
        string guidance
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(code, "private child stdout", "private child stderr"));
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        var action = () =>
            client.VerifyDevelopmentCertificateActivationAsync("actual", CancellationToken.None);

        // Assert
        var failure = (await action.Should().ThrowAsync<AboxException>()).Which;
        failure.Message.Should().Contain(guidance).And.NotContain("private child");
        var observed = (ProcessRequest)
            runner.ReceivedCalls().Should().ContainSingle().Which.GetArguments()[0]!;
        observed.Arguments.Should().Contain("status").And.NotContain("root").And.NotContain("-i");
        observed.StandardInput.Should().BeEmpty();
        observed.OutputPolicy.Should().Be(ProcessOutputPolicy.Sensitive);
        observed.Environment.Should().BeNull();
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1000\n2000")]
    [InlineData("1e3")]
    public async Task InvalidApplicationIdentityStopsBeforeRootTrustOrPrivateActivation(string uid)
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var pfx = SyntheticCertificatePayload();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, uid, "synthetic private identity"));
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        var action = () =>
            client.ActivateDevelopmentCertificateAsync(
                "actual",
                pfx,
                "password",
                CancellationToken.None
            );

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.ToString()
            .Should()
            .Contain("sandbox trust")
            .And.NotContain("synthetic private");
        var observed = (ProcessRequest)
            runner.ReceivedCalls().Should().ContainSingle().Which.GetArguments()[0]!;
        observed.Arguments.Should().Equal("exec", "actual", "id", "-u");
        observed.OutputPolicy.Should().Be(ProcessOutputPolicy.CapturedSensitive);
    }

    private static byte[] SyntheticCertificatePayload()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=localhost",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1
        );
        using var certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow.AddDays(1)
        );
        return certificate.Export(X509ContentType.Pfx, "password");
    }

    [Theory]
    [InlineData("prepare")]
    [InlineData("activate")]
    [InlineData("refresh-bootstrap")]
    public async Task RuntimeLockTimeoutGivesRetryGuidanceWithoutLeakingChildDiagnostics(
        string mode
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var arguments = call.Arg<ProcessRequest>().Arguments;
                if (arguments.Contains("id"))
                    return new ProcessResult(0, "1000\n", "");
                return new ProcessResult(
                    arguments.Contains("cleanup") || arguments.Contains("root") ? 0 : 75,
                    "private child stdout",
                    "private child stderr"
                );
            });
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        Func<Task> action =
            mode == "prepare"
                ? async () =>
                {
                    await client.GetDevelopmentCertificatePasswordAsync(
                        "actual",
                        CancellationToken.None
                    );
                }
            : mode == "refresh-bootstrap"
                ? () =>
                    client.RefreshDevelopmentCertificateBootstrapAsync(
                        "actual",
                        CancellationToken.None
                    )
            : () =>
                client.ActivateDevelopmentCertificateAsync(
                    "actual",
                    SyntheticCertificatePayload(),
                    "password",
                    CancellationToken.None
                );

        // Assert
        var failure = (await action.Should().ThrowAsync<AboxException>()).Which;
        failure
            .Message.Should()
            .Contain("busy")
            .And.Contain("retry")
            .And.NotContain("private child");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BootstrapRefreshFailureAndCancellationNeverExposeRawDiagnostics(bool cancel)
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<ProcessResult>(
                    cancel
                        ? new OperationCanceledException("private child diagnostics")
                        : new AboxException("private child diagnostics")
                )
            );
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        var action = () =>
            client.RefreshDevelopmentCertificateBootstrapAsync("actual", CancellationToken.None);

        // Assert
        var failure = cancel
            ? (Exception)(await action.Should().ThrowAsync<OperationCanceledException>()).Which
            : (await action.Should().ThrowAsync<AboxException>()).Which;
        failure.Message.Should().Contain("bootstrap").And.NotContain("private child");
    }
}
