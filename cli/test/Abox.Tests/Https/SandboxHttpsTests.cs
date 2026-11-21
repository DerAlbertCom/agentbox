using Abox.Configuration;
using Abox.Https;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SandboxHttpsTests
{
    [Fact]
    public async Task LaunchReadinessFailureRefusesWithRecoveryGuidance()
    {
        // Arrange
        using var root = new CertificateTestDirectory();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(78, "", ""));
        var https = Create(runner, root.Path);

        // Act
        var action = () => https.EnsureLaunchReadyAsync("actual", CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .Which.Message.Should()
            .Be(
                "Herdr was not opened: sandbox HTTPS launch readiness failed. Exit stale agents and explicitly initialize fresh core/service parents through abox after saving work; rerun 'abox ports' for recovery guidance. No core or app was restarted."
            );
    }

    [Fact]
    public async Task LaunchReadinessCrossesTheProtectedBootstrapInTheSandbox()
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
                return new ProcessResult(0, "", "");
            });
        var https = Create(runner, root.Path);

        // Act
        await https.EnsureLaunchReadyAsync("actual", CancellationToken.None);

        // Assert
        observed!.FileName.Should().Be("sbx");
        observed.Arguments.Should().StartWith(["exec", "actual", "sh", "-c"]);
        observed.Arguments.Should().Contain("abox-https");
        observed.Arguments[^1].Should().Be("true");
    }

    private static SandboxHttps Create(IProcessRunner runner, string root)
    {
        var console = new TestConsole();
        return new SandboxHttps(
            new HostDevelopmentCertificate(runner, console, root, []),
            new SbxClient(runner, AboxPaths.For(root, root)),
            console
        );
    }
}
