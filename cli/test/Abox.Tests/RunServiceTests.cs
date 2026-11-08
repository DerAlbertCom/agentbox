using Abox.Build;
using Abox.Configuration;
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
    private readonly AboxPaths _paths;
    private readonly BuildStore _store;
    private readonly SandboxRecordStore _records;
    private readonly CreateService _create;

    public RunServiceTests()
    {
        var launchers = new LauncherRegistry([new ShellLauncher()]);
        _paths = AboxPaths.For(
            Path.Combine(_root.Path, "config"),
            Path.Combine(_root.Path, "state")
        );
        _store = new BuildStore(_paths);
        _records = new SandboxRecordStore(_paths);
        _create = new CreateService(_store, _records, _paths, _sbx, _console);
        _service = new RunService(
            launchers,
            _store,
            _records,
            _create,
            _sbx,
            new StubHostPrograms(),
            _console
        );
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

    [Fact]
    public async Task ANewerBuildIsOfferedAndReplacesTheSandbox()
    {
        var name = SandboxName.For(_root.Path);
        _sbx.Sandboxes.Add(new SandboxInfo(name, "running", DateTimeOffset.Now));
        _records.Save(
            name,
            new SandboxRecord
            {
                Squad = "ac",
                Tag = "latest",
                BuildCreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            }
        );
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                CreatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                WorkloadKitDirectory = "/kits/workload",
            }
        );

        var exitCode = await _service.RunAsync(
            new RunRequest("shell", _root.Path, []),
            CancellationToken.None
        );

        Assert.Equal(0, exitCode);
        Assert.Contains("newer build", _console.AllText);
        Assert.Equal(name, Assert.Single(_sbx.Removed));
    }

    [Fact]
    public async Task ANewerBuildIsNotOfferedWhenTheAnswerIsNo()
    {
        var name = SandboxName.For(_root.Path);
        _console.Answer = false;
        _sbx.Sandboxes.Add(new SandboxInfo(name, "running", DateTimeOffset.Now));
        _records.Save(
            name,
            new SandboxRecord
            {
                Squad = "ac",
                Tag = "latest",
                BuildCreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            }
        );
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                CreatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
            }
        );

        await _service.RunAsync(new RunRequest("shell", _root.Path, []), CancellationToken.None);

        Assert.Empty(_sbx.Removed);
    }

    private sealed class StubHostPrograms : Abox.Infrastructure.IHostPrograms
    {
        public bool Exists(string program) => true;
    }
}
