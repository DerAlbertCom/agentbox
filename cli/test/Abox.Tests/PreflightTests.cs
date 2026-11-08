using Abox.Health;
using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Tests;

public sealed class PreflightTests
{
    private readonly TestConsole _console = new();
    private readonly FakeSbxClient _sbx = new();

    [Fact]
    public async Task WithoutSbxTheInstallStepsArePrinted()
    {
        var preflight = new Preflight(_console, _sbx, new StubHostPrograms(false));

        var ready = await preflight.EnsureReadyAsync(CancellationToken.None);

        Assert.False(ready);
        Assert.Contains("not on PATH", _console.AllText);
        Assert.Contains("sbx", _console.AllText);
    }

    [Fact]
    public async Task AnOldSbxIsRefused()
    {
        _sbx.Version = new Version(0, 44, 0);
        var preflight = new Preflight(_console, _sbx, new StubHostPrograms(true));

        var ready = await preflight.EnsureReadyAsync(CancellationToken.None);

        Assert.False(ready);
        Assert.Contains("too old", _console.AllText);
    }

    [Fact]
    public async Task AFailingCheckStopsTheCommand()
    {
        _sbx.Report = new DiagnoseReport([
            new DiagnosticCheck("Authentication", "pass", "authenticated", ""),
            new DiagnosticCheck("Daemon", "fail", "not healthy", "start the daemon"),
        ]);
        var preflight = new Preflight(_console, _sbx, new StubHostPrograms(true));

        var ready = await preflight.EnsureReadyAsync(CancellationToken.None);

        Assert.False(ready);
        Assert.Contains("start the daemon", _console.AllText);
    }

    [Fact]
    public async Task AHealthySbxLetsTheCommandGoOn()
    {
        _sbx.Report = new DiagnoseReport([
            new DiagnosticCheck("Authentication", "pass", "authenticated", ""),
            new DiagnosticCheck("Daemon", "pass", "healthy", ""),
        ]);
        var preflight = new Preflight(_console, _sbx, new StubHostPrograms(true));

        var ready = await preflight.EnsureReadyAsync(CancellationToken.None);

        Assert.True(ready);
    }

    private sealed class StubHostPrograms(bool exists) : IHostPrograms
    {
        public bool Exists(string program) => exists;
    }
}
