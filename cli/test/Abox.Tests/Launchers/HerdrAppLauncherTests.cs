using Abox.Infrastructure;
using Abox.Launchers;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class HerdrAppLauncherTests
{
    [Fact]
    public async Task StaleSandboxParentDoesNotOpenDesktopOrReportSuccessfulLaunch()
    {
        // Arrange
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "actual", ""));
        var sbx = Substitute.For<ISbxClient>();
        sbx.ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>()).Returns(42);
        var launcher = new HerdrAppLauncher(runner);
        var context = new LauncherContext(
            "location",
            "actual",
            "/workspace",
            [],
            sbx,
            new TestConsole()
        );

        // Act
        var action = () => launcher.RunAsync(context, CancellationToken.None);

        // Assert
        var failure = (await action.Should().ThrowAsync<AboxException>()).Which;
        failure.Message.Should().Contain("fresh").And.Contain("not opened");
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }
}
