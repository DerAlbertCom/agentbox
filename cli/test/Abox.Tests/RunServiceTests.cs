using Abox.Launchers;
using Abox.Naming;
using Abox.Sbx;
using Abox.Services;

namespace Abox.Tests;

public sealed class RunServiceTests
{
    private readonly TempDirectory _root = new();
    private readonly TestConsole _console = new();
    private readonly FakeSbxClient _sbx = new();
    private readonly RunService _service;

    public RunServiceTests()
    {
        var launchers = new LauncherRegistry([new ShellLauncher()]);
        _service = new RunService(launchers, _sbx, new StubHostPrograms(), _console);
    }

    [Fact]
    public async Task TheShellRunsInTheWorkspaceOfTheSandbox()
    {
        _sbx.Sandboxes.Add(
            new SandboxInfo(SandboxName.For(_root.Path), "running", DateTimeOffset.Now)
        );
        _sbx.WorkspaceDirectory = "/home/agent/workspace";

        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        Assert.Equal(0, exitCode);
        var exec = Assert.Single(_sbx.Executed);
        Assert.Equal(["bash", "-l"], exec.Command);
        Assert.Equal("/home/agent/workspace", exec.WorkingDirectory);
        Assert.True(exec.AllocateTty);
    }

    [Fact]
    public async Task TheShellRunsInTheFolderWhenTheRuntimeDoesNotSayWhereTheWorkspaceIs()
    {
        _sbx.Sandboxes.Add(
            new SandboxInfo(SandboxName.For(_root.Path), "running", DateTimeOffset.Now)
        );

        await _service.RunAsync(new RunRequest("shell", _root.Path, []), CancellationToken.None);

        Assert.Equal(_root.Path, Assert.Single(_sbx.Executed).WorkingDirectory);
    }

    [Fact]
    public async Task TheShellRunsTheCommandThatIsGiven()
    {
        _sbx.Sandboxes.Add(
            new SandboxInfo(SandboxName.For(_root.Path), "running", DateTimeOffset.Now)
        );

        await _service.RunAsync(
            new RunRequest("shell", _root.Path, ["ls", "-la"]),
            CancellationToken.None
        );

        Assert.Equal(["ls", "-la"], Assert.Single(_sbx.Executed).Command);
    }

    [Fact]
    public async Task WithoutASandboxTheCreateCommandIsPrinted()
    {
        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        Assert.Equal(1, exitCode);
        Assert.Contains("abox create", _console.AllText);
        Assert.Empty(_sbx.Executed);
    }

    [Fact]
    public async Task ALauncherThatIsNotKnownIsAnError()
    {
        var exception = await Assert.ThrowsAsync<AboxException>(() =>
            _service.RunAsync(new RunRequest("nope", _root.Path, []), CancellationToken.None)
        );

        Assert.Contains("no launcher 'nope'", exception.Message);
    }

    private sealed class StubHostPrograms : Abox.Infrastructure.IHostPrograms
    {
        public bool Exists(string program) => true;
    }
}
