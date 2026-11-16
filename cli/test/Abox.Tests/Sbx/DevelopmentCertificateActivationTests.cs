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
        observed.Arguments.Should().Contain("python3");
        observed.Arguments.TakeLast(command.Length).Should().Equal(command);
        observed.Environment.Should().BeNull();
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
}
