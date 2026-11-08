using Abox.Build;
using Abox.Configuration;
using Abox.Naming;
using Abox.Sbx;
using Abox.Services;
using NSubstitute;

namespace Abox.Tests;

public sealed class CreateServiceTests
{
    private readonly TempDirectory _root = new();
    private readonly TestConsole _console = new();
    private readonly ISbxClient _sbx = Substitute.For<ISbxClient>();
    private readonly AboxPaths _paths;
    private readonly BuildStore _store;
    private readonly SandboxRecordStore _records;
    private readonly CreateService _service;

    public CreateServiceTests()
    {
        _paths = AboxPaths.For(
            Path.Combine(_root.Path, "config"),
            Path.Combine(_root.Path, "state")
        );
        _store = new BuildStore(_paths);
        _records = new SandboxRecordStore(_paths);
        _service = new CreateService(_store, _records, _paths, _sbx, _console);

        _sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
        _sbx.RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
        _sbx.ListMcpServersAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);
        _sbx.AddMcpServerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
    }

    [Fact]
    public async Task ASandboxIsCreatedFromTheLocalBuild()
    {
        SaveBuild();

        var exitCode = await _service.CreateAsync(Request(), CancellationToken.None);

        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource == "/kits/workload"
                    && request.Name == SandboxName.For(_root.Path)
                    && request.Mixins.Contains("/kits/pi-config")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ABuildThatIsNotThereIsAnError()
    {
        var action = () => _service.CreateAsync(Request(), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("abox build --squad ac");
    }

    [Fact]
    public async Task AnExistingSandboxIsReplacedAfterConfirmation()
    {
        SaveBuild();
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));

        var exitCode = await _service.CreateAsync(Request(), CancellationToken.None);

        exitCode.Should().Be(0);
        await _sbx.Received(1).RemoveSandboxAsync(name, Arg.Any<CancellationToken>());
        _console.AllText.Should().Contain("already exists");
    }

    [Fact]
    public async Task AnExistingSandboxIsKeptWhenTheAnswerIsNo()
    {
        SaveBuild();
        _console.Answer = false;
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));

        var action = () => _service.CreateAsync(Request(), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("not replaced");
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheStateDirectoriesAreKeptOnTheHostUnderTheNameOfTheSandbox()
    {
        SaveBuild();

        await _service.CreateAsync(Request(), CancellationToken.None);

        var name = SandboxName.For(_root.Path);
        var hostPath = Path.Combine(_paths.StateDirectory, name, ".config", "opencode", "state");
        Directory.Exists(hostPath).Should().BeTrue();

        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.Mounts.Count == 1
                    && request.Mounts[0].HostPath == hostPath
                    && request.Mounts[0].SandboxPath == "/home/agent/.config/opencode/state"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task TheMcpServersAreRegisteredWhenTheyHaveAnUrl()
    {
        SaveBuild(mcp: [new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" }]);

        await _service.CreateAsync(Request(), CancellationToken.None);

        await _sbx.Received(1)
            .AddMcpServerAsync("elastic", "https://example.com/mcp", Arg.Any<CancellationToken>());
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.StaticMcpServers.SequenceEqual(new[] { "elastic" })
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AnMcpServerWithoutAnUrlHasToBeRegisteredAlready()
    {
        SaveBuild(mcp: [new ManifestMcp { Name = "elastic" }]);

        var action = () => _service.CreateAsync(Request(), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("not registered");
    }

    [Fact]
    public async Task TheRecordOfTheSandboxIsKept()
    {
        SaveBuild();

        await _service.CreateAsync(Request(), CancellationToken.None);

        var record = _records.Load(SandboxName.For(_root.Path));
        record.Should().NotBeNull();
        record!.Squad.Should().Be("ac");
        record.Tag.Should().Be("latest");
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
