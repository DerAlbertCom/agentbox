using Abox.Build;
using Abox.Configuration;
using Abox.Naming;
using Abox.Sbx;
using Abox.Services;

namespace Abox.Tests;

public sealed class CreateServiceTests
{
    private readonly TempDirectory _root = new();
    private readonly TestConsole _console = new();
    private readonly FakeSbxClient _sbx = new();
    private readonly AboxPaths _paths;
    private readonly BuildStore _store;
    private readonly CreateService _service;

    public CreateServiceTests()
    {
        _paths = AboxPaths.For(
            Path.Combine(_root.Path, "config"),
            Path.Combine(_root.Path, "state")
        );
        _store = new BuildStore(_paths);
        _service = new CreateService(_store, _paths, _sbx, _console);
    }

    [Fact]
    public async Task ASandboxIsCreatedFromTheLocalBuild()
    {
        SaveBuild();

        var exitCode = await _service.CreateAsync(Request(), CancellationToken.None);

        Assert.Equal(0, exitCode);
        var request = Assert.Single(_sbx.Created);
        Assert.Equal("/kits/workload", request.KitSource);
        Assert.Equal(SandboxName.For(_root.Path), request.Name);
        Assert.Contains("/kits/pi-config", request.Mixins);
    }

    [Fact]
    public async Task ABuildThatIsNotThereIsAnError()
    {
        var exception = await Assert.ThrowsAsync<AboxException>(() =>
            _service.CreateAsync(Request(), CancellationToken.None)
        );

        Assert.Contains("abox build --squad ac", exception.Message);
    }

    [Fact]
    public async Task AnExistingSandboxIsReplacedAfterConfirmation()
    {
        SaveBuild();
        var name = SandboxName.For(_root.Path);
        _sbx.Sandboxes.Add(new SandboxInfo(name, "running", DateTimeOffset.Now));

        var exitCode = await _service.CreateAsync(Request(), CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(name, Assert.Single(_sbx.Removed));
        Assert.Contains("already exists", _console.AllText);
    }

    [Fact]
    public async Task AnExistingSandboxIsKeptWhenTheAnswerIsNo()
    {
        SaveBuild();
        _console.Answer = false;
        _sbx.Sandboxes.Add(
            new SandboxInfo(SandboxName.For(_root.Path), "running", DateTimeOffset.Now)
        );

        var exception = await Assert.ThrowsAsync<AboxException>(() =>
            _service.CreateAsync(Request(), CancellationToken.None)
        );

        Assert.Contains("not replaced", exception.Message);
        Assert.Empty(_sbx.Removed);
        Assert.Empty(_sbx.Created);
    }

    [Fact]
    public async Task TheStateDirectoriesAreKeptOnTheHostUnderTheNameOfTheSandbox()
    {
        SaveBuild();

        await _service.CreateAsync(Request(), CancellationToken.None);

        var name = SandboxName.For(_root.Path);
        var hostPath = Path.Combine(_paths.StateDirectory, name, ".config", "opencode", "state");
        Assert.True(Directory.Exists(hostPath));

        var mount = Assert.Single(Assert.Single(_sbx.Created).Mounts);
        Assert.Equal(hostPath, mount.HostPath);
        Assert.Equal("/home/agent/.config/opencode/state", mount.SandboxPath);
    }

    [Fact]
    public async Task TheMcpServersAreRegisteredWhenTheyHaveAnUrl()
    {
        SaveBuild(mcp: [new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" }]);

        await _service.CreateAsync(Request(), CancellationToken.None);

        Assert.Equal(["elastic"], _sbx.RegisteredMcpServers);
        Assert.Equal(["elastic"], Assert.Single(_sbx.Created).StaticMcpServers);
    }

    [Fact]
    public async Task AnMcpServerWithoutAnUrlHasToBeRegisteredAlready()
    {
        SaveBuild(mcp: [new ManifestMcp { Name = "elastic" }]);

        var exception = await Assert.ThrowsAsync<AboxException>(() =>
            _service.CreateAsync(Request(), CancellationToken.None)
        );

        Assert.Contains("not registered", exception.Message);
    }

    private CreateRequest Request() => new("ac", _root.Path, "latest");

    private void SaveBuild(List<ManifestMcp>? mcp = null)
    {
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                WorkloadKitDirectory = "/kits/workload",
                Kits =
                [
                    new ManifestKit
                    {
                        Name = "pi-config",
                        Reference = "/kits/pi-config",
                        IsLocal = true,
                    },
                ],
                State =
                [
                    new ManifestState
                    {
                        SandboxPath = "/home/agent/.config/opencode/state",
                        RelativePath = ".config/opencode/state",
                    },
                ],
                Mcp = mcp ?? [],
            }
        );
    }
}
