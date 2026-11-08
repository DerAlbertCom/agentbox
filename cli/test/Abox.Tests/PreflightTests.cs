using Abox.Health;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class PreflightTests
{
    private readonly TestConsole _console = new();

    [Fact]
    public async Task WithoutSbxTheInstallStepsArePrinted()
    {
        var sbx = Substitute.For<ISbxClient>();
        var hostPrograms = Substitute.For<IHostPrograms>();
        hostPrograms.Exists(Arg.Any<string>()).Returns(false);

        var preflight = new Preflight(_console, sbx, hostPrograms);

        var ready = await preflight.EnsureReadyAsync(CancellationToken.None);

        ready.Should().BeFalse();
        _console.AllText.Should().Contain("not on PATH");
        _console.AllText.Should().Contain("sbx");
    }

    [Fact]
    public async Task AnOldSbxIsRefused()
    {
        var sbx = Substitute.For<ISbxClient>();
        sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 44, 0));
        var hostPrograms = Substitute.For<IHostPrograms>();
        hostPrograms.Exists(Arg.Any<string>()).Returns(true);

        var preflight = new Preflight(_console, sbx, hostPrograms);

        var ready = await preflight.EnsureReadyAsync(CancellationToken.None);

        ready.Should().BeFalse();
        _console.AllText.Should().Contain("too old");
    }

    [Fact]
    public async Task AFailingCheckStopsTheCommand()
    {
        var sbx = Substitute.For<ISbxClient>();
        sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 51, 0));
        sbx.DiagnoseAsync(Arg.Any<CancellationToken>())
            .Returns(
                new DiagnoseReport([
                    new DiagnosticCheck("Authentication", "pass", "authenticated", ""),
                    new DiagnosticCheck("Daemon", "fail", "not healthy", "start the daemon"),
                ])
            );
        var hostPrograms = Substitute.For<IHostPrograms>();
        hostPrograms.Exists(Arg.Any<string>()).Returns(true);

        var preflight = new Preflight(_console, sbx, hostPrograms);

        var ready = await preflight.EnsureReadyAsync(CancellationToken.None);

        ready.Should().BeFalse();
        _console.AllText.Should().Contain("start the daemon");
    }

    [Fact]
    public async Task AHealthySbxLetsTheCommandGoOn()
    {
        var sbx = Substitute.For<ISbxClient>();
        sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 51, 0));
        sbx.DiagnoseAsync(Arg.Any<CancellationToken>())
            .Returns(
                new DiagnoseReport([
                    new DiagnosticCheck("Authentication", "pass", "authenticated", ""),
                    new DiagnosticCheck("Daemon", "pass", "healthy", ""),
                ])
            );
        var hostPrograms = Substitute.For<IHostPrograms>();
        hostPrograms.Exists(Arg.Any<string>()).Returns(true);

        var preflight = new Preflight(_console, sbx, hostPrograms);

        var ready = await preflight.EnsureReadyAsync(CancellationToken.None);

        ready.Should().BeTrue();
    }
}
