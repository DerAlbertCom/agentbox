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
    [InlineData("cancel", true)]
    [InlineData("cancel", false)]
    public async Task FailuresSuppressRawDiagnosticsAndAttemptBoundedSensitiveCleanup(
        string failure,
        bool cleanupSucceeds
    )
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        var calls = new List<ProcessRequest>();
        const string privateText = "synthetic-password 123456789 true";
        using var cancellation = new CancellationTokenSource();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                calls.Add(request);
                if (request.Arguments.Contains("cleanup"))
                {
                    call.Arg<CancellationToken>().CanBeCanceled.Should().BeTrue();
                    call.Arg<CancellationToken>().IsCancellationRequested.Should().BeFalse();
                    return Task.FromResult(
                        new ProcessResult(cleanupSucceeds ? 0 : 1, privateText, privateText)
                    );
                }
                if (failure == "cancel")
                {
                    cancellation.Cancel();
                    return Task.FromException<ProcessResult>(
                        new OperationCanceledException(privateText, cancellation.Token)
                    );
                }
                return failure == "exit"
                    ? Task.FromResult(new ProcessResult(17, privateText, privateText))
                    : Task.FromException<ProcessResult>(new IOException(privateText));
            });
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));

        // Act
        var action = () =>
            client.StageDevelopmentCertificateAsync(
                "actual",
                new byte[] { 1, 2 },
                "synthetic-password",
                cancellation.Token
            );

        // Assert
        var error =
            failure == "cancel"
                ? (Exception)(await action.Should().ThrowAsync<OperationCanceledException>()).Which
                : (await action.Should().ThrowAsync<AboxException>()).Which;
        error.ToString().Should().NotContain(privateText).And.NotContain("synthetic-password");
        error.InnerException.Should().BeNull();
        calls.Should().HaveCount(2);
        var cleanup = calls[1];
        cleanup.Arguments.Should().NotContain("-i");
        cleanup.StandardInput.Should().BeEmpty();
        cleanup.OutputPolicy.Should().Be(ProcessOutputPolicy.Sensitive);
        cleanup.Arguments[^1].Should().Be(calls[0].Arguments[^1]);
        if (!cleanupSucceeds)
            error.Message.Should().Contain("cleanup is unconfirmed");
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

    [Fact]
    public async Task AlreadyCanceledCopyDoesNotStartASandboxEvenForCleanup()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var action = () =>
            client.StageDevelopmentCertificateAsync(
                "actual",
                new byte[] { 1 },
                "synthetic-password",
                cancellation.Token
            );

        // Assert
        await action.Should().ThrowAsync<OperationCanceledException>();
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }
}
