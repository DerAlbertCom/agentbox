using Abox.Build;
using Abox.Configuration;
using Abox.Features.Build;
using Abox.Features.Create;
using Abox.Features.Prune;
using Abox.Features.Run;
using Abox.Features.Secrets;
using Abox.Kits;
using Abox.Naming;
using Abox.Sbx;
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
        _service = new CreateService(_store, _records, _paths, new AboxConfig(), _sbx, _console);

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
                    && request.PullPolicy == "never"
                    && request.Name == SandboxName.For(_root.Path)
                    && request.Mixins.Contains("/kits/pi-config")
                    && request.Mixins.Contains("/kits/state")
                    && request.KitArguments["mode"] == "team"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("mtiae-docker-release")]
    [InlineData("team-docker-release")]
    public async Task PublishedSetsArePulledFromTheConfiguredRepository(string repository)
    {
        SaveBuild(mcp: [new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" }]);
        var service = new CreateService(
            _store,
            _records,
            _paths,
            new AboxConfig { PullRepository = repository },
            _sbx,
            _console
        );

        var exitCode = await service.CreateAsync(
            Request() with
            {
                Local = false,
            },
            CancellationToken.None
        );

        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource
                        == $"rwest.jfrog.io/{repository}/agentbox-rwest-sample/ac:latest"
                    && request.PullPolicy == "always"
                    && request.Mixins.Count == 0
                    && request.KitArguments.Count == 0
                    && request.Mounts.Count == 1
                    && request.StaticMcpServers.SequenceEqual(new[] { "elastic" })
                ),
                Arg.Any<CancellationToken>()
            );
        _records
            .Load(SandboxName.For(_root.Path))!
            .KitReference.Should()
            .Be($"rwest.jfrog.io/{repository}/agentbox-rwest-sample/ac:latest");
    }

    [Fact]
    public async Task PullFailuresAreReportedWithoutFallingBackToLocalKits()
    {
        SaveBuild();
        _sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(1, "registry access denied"));

        var action = () =>
            _service.CreateAsync(Request() with { Local = false }, CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*registry access denied*");
        await _sbx.Received(1)
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
        _records.Load(SandboxName.For(_root.Path)).Should().BeNull();
    }

    [Theory]
    [InlineData("bad/path", "sample", "ac", "latest", "pullRepository")]
    [InlineData("release", "bad/team", "ac", "latest", "team/squad")]
    [InlineData("release", "sample", "bad/squad", "latest", "team/squad")]
    [InlineData("release", "sample", "ac", "bad/tag", "registry tag")]
    public async Task InvalidPullReferencesFailBeforeAnySandboxIsTouched(
        string repository,
        string team,
        string squad,
        string tag,
        string error
    )
    {
        var service = new CreateService(
            _store,
            _records,
            _paths,
            new AboxConfig { PullRepository = repository },
            _sbx,
            _console
        );
        var request = new CreateRequest(
            new SquadResolver.ResolvedTeam(team, "teams/sample", [squad]),
            squad,
            _root.Path,
            tag
        );

        var action = () => service.CreateAsync(request, CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage($"*{error}*");
        await _sbx.DidNotReceive()
            .FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("other-team")]
    [InlineData("")]
    public async Task PullingRequiresMetadataFromTheSelectedTeam(string team)
    {
        SaveBuild(reference: team == "" ? "" : ImageName.OfTeam(team, "latest"));

        var action = () =>
            _service.CreateAsync(Request() with { Local = false }, CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*metadata*team 'sample'*abox build*"
        );
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOnlySquadOfTheTeamIsUsedWithoutAsking()
    {
        SaveBuild();
        var request = new CreateRequest(
            new SquadResolver.ResolvedTeam("sample", "teams/sample", ["ac"]),
            null,
            _root.Path,
            "latest",
            Local: true
        );

        var exitCode = await _service.CreateAsync(request, CancellationToken.None);

        exitCode.Should().Be(0);
        _console.AllText.Should().NotContain("Which squad");
        _records.Load(SandboxName.For(_root.Path))!.Squad.Should().Be("ac");
    }

    [Fact]
    public async Task TheSquadIsAskedForWhenTheTeamHasMoreThanOne()
    {
        SaveBuild();
        _console.Picks.Add(0);
        var request = new CreateRequest(
            new SquadResolver.ResolvedTeam("sample", "teams/sample", ["ac", "bd"]),
            null,
            _root.Path,
            "latest",
            Local: true
        );

        var exitCode = await _service.CreateAsync(request, CancellationToken.None);

        exitCode.Should().Be(0);
        _console.AllText.Should().Contain("Which squad of the team 'sample'");
        _records.Load(SandboxName.For(_root.Path))!.Squad.Should().Be("ac");
    }

    [Fact]
    public async Task ABuildThatIsNotThereIsAnError()
    {
        var action = () => _service.CreateAsync(Request(), CancellationToken.None);

        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("abox build --team sample");
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
                    && Path.GetFullPath(request.Mounts[0].HostPath) == Path.GetFullPath(hostPath)
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

    private CreateRequest Request() =>
        new(
            new SquadResolver.ResolvedTeam("sample", "teams/sample", ["ac"]),
            "ac",
            _root.Path,
            "latest",
            Local: true
        );

    private void SaveBuild(List<ManifestMcp>? mcp = null, string? reference = null)
    {
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = reference ?? ImageName.OfTeam("sample", "latest"),
                WorkloadKitDirectory = "/kits/workload",
                StateMixinDirectory = "/kits/state",
                Kits =
                [
                    new ManifestKit
                    {
                        Name = "pi-config",
                        Reference = "/kits/pi-config",
                        IsLocal = true,
                        Arguments = new Dictionary<string, string> { ["mode"] = "team" },
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
