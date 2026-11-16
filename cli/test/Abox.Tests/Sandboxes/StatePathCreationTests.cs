using Abox.Configuration;
using Abox.Features.Build;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sandboxes;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

public sealed class StatePathCreationTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly ISbxClient _sbx = Substitute.For<ISbxClient>();
    private readonly IPublishedSquadSource _published = PublishedSquadTest.Source();
    private readonly AboxPaths _paths;
    private readonly BuildStore _store;
    private readonly SandboxRecordStore _records;
    private readonly SandboxCreator _create;
    private readonly TestConsole _console = new();

    public StatePathCreationTests()
    {
        _paths = AboxPaths.For(_root.Directory_("config"), Path.Combine(_root.Path, "state"));
        _store = new BuildStore(_paths);
        _records = new SandboxRecordStore(_paths);
        _create = new SandboxCreator(
            _records,
            _paths,
            SandboxTest.Resolver(_sbx),
            _sbx,
            _console,
            new LocalSquadSource(_store, LocalWorkloadTest.Images(), _console),
            new RemoteSquadSource(new AboxConfig(), _sbx, _published),
            new UserSecretsCopy(new AboxConfig(), new UserSecretsHost(null, null), _sbx),
            SandboxTest.Ports(_sbx, _console)
        );
        _sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        _sbx.RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        _sbx.AddMcpServerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        _sbx.ListMcpServersAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);
    }

    [Theory]
    [InlineData("../outside", true)]
    [InlineData("..\\outside", true)]
    [InlineData("different", true)]
    [InlineData("rooted-in-fixture", true)]
    [InlineData("../outside", false)]
    [InlineData("..\\outside", false)]
    [InlineData("different", false)]
    [InlineData("rooted-in-fixture", false)]
    public async Task InvalidStateIsRejectedAsAWholeBeforePreparationOrReplacement(
        string relativePath,
        bool local
    )
    {
        // Arrange
        if (relativePath == "rooted-in-fixture")
            relativePath = _root.Directory_("outside");
        var manifest = SaveBuild([
            new ManifestState { SandboxPath = "/home/agent/.valid", RelativePath = ".valid" },
            new ManifestState { SandboxPath = "/home/agent/outside", RelativePath = relativePath },
        ]);
        _published
            .LoadAsync(Arg.Any<string>(), "sample", "ac", "latest", Arg.Any<CancellationToken>())
            .Returns(new PublishedSquad("pinned-set", manifest));
        var name = SandboxName.For(_root.Path);
        var sentinel = _root.File("state/outside/sentinel.txt", "persistent data");
        var before = Directory.GetFileSystemEntries(
            _paths.StateDirectory,
            "*",
            SearchOption.AllDirectories
        );
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.UtcNow));

        // Act
        var action = () =>
            _create.CreateAsync(Request() with { Local = local }, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*state*");
        File.ReadAllText(sentinel).Should().Be("persistent data");
        Directory
            .GetFileSystemEntries(_paths.StateDirectory, "*", SearchOption.AllDirectories)
            .Should()
            .BeEquivalentTo(before);
        _sbx.ReceivedCalls()
            .Where(call =>
                call.GetMethodInfo().Name != nameof(ISbxClient.EnsureKitSourceAllowedAsync)
            )
            .Should()
            .BeEmpty("invalid state must fail even before MCP lookup or replacement discovery");
        _records.Load(name).Should().BeNull();
    }

    [Theory]
    [InlineData("null-list")]
    [InlineData("null-entry")]
    [InlineData("null-sandbox-path")]
    [InlineData("null-relative-path")]
    public async Task IncompleteStoredStateFailsActionablyBeforeRuntimeAccess(string error)
    {
        // Arrange
        SaveBuild([
            new ManifestState { SandboxPath = "/home/agent/.cache", RelativePath = ".cache" },
        ]);
        var manifest = _store.Load("sample", "ac", "latest")!;
        switch (error)
        {
            case "null-list":
                manifest.State = null!;
                break;
            case "null-entry":
                manifest.State.Add(null!);
                break;
            case "null-sandbox-path":
                manifest.State[0].SandboxPath = null!;
                break;
            default:
                manifest.State[0].RelativePath = null!;
                break;
        }
        _store.Save(manifest);

        // Act
        var action = () => _create.CreateAsync(Request(), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*state*");
        _sbx.ReceivedCalls().Should().BeEmpty();
        Directory
            .Exists(Path.Combine(_paths.StateDirectory, SandboxName.For(_root.Path)))
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData("/home/agent//.cache", ".cache")]
    [InlineData("/home/agent/.cache/", ".cache")]
    [InlineData("/home/agent/./.cache", ".cache")]
    [InlineData("/home/agent/../outside", "outside")]
    [InlineData("/home/agent\\outside", "home/agent\\outside")]
    [InlineData("~/.cache", ".cache")]
    [InlineData("/", "")]
    public async Task StoredStateIsNeverSilentlyRepairedOrRelocated(
        string sandboxPath,
        string relativePath
    )
    {
        // Arrange
        SaveBuild([new ManifestState { SandboxPath = sandboxPath, RelativePath = relativePath }]);

        // Act
        var action = () => _create.CreateAsync(Request(), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*state*");
        _sbx.ReceivedCalls().Should().BeEmpty();
        Directory
            .Exists(Path.Combine(_paths.StateDirectory, SandboxName.For(_root.Path)))
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData("/home/agent/.cache", ".cache")]
    [InlineData("/home/agent/.cache/sessions", ".cache/sessions")]
    [InlineData("/home/agent/.CACHE", ".CACHE")]
    [InlineData("/.cache", ".cache")]
    [InlineData("/.cache/sessions", ".cache/sessions")]
    public async Task StoredStateCannotBypassDuplicateOrOverlapChecks(
        string sandboxPath,
        string relativePath
    )
    {
        // Arrange
        SaveBuild([
            new ManifestState { SandboxPath = "/home/agent/.cache", RelativePath = ".cache" },
            new ManifestState { SandboxPath = sandboxPath, RelativePath = relativePath },
        ]);

        // Act
        var action = () => _create.CreateAsync(Request(), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*state*overlap*");
        _sbx.ReceivedCalls().Should().BeEmpty();
        Directory
            .Exists(Path.Combine(_paths.StateDirectory, SandboxName.For(_root.Path)))
            .Should()
            .BeFalse();
    }

    [Theory]
    [InlineData(
        "~/.config/opencode/state",
        "/home/agent/.config/opencode/state",
        ".config/opencode/state"
    )]
    [InlineData(
        "/home/agent/.config/opencode/state",
        "/home/agent/.config/opencode/state",
        ".config/opencode/state"
    )]
    [InlineData("/var/lib/cache", "/var/lib/cache", "var/lib/cache")]
    [InlineData("~", "/home/agent", "home/agent")]
    [InlineData("~/", "/home/agent", "home/agent")]
    [InlineData("/home/agent", "/home/agent", "home/agent")]
    [InlineData("/home/agent2", "/home/agent2", "home/agent2")]
    [InlineData("~////.cache////sessions/", "/home/agent/.cache/sessions", ".cache/sessions")]
    [InlineData("/var////lib/cache/", "/var/lib/cache", "var/lib/cache")]
    public async Task BuildAndRecreatePreserveSupportedStateMappingsAndContents(
        string declaration,
        string sandboxPath,
        string relativePath
    )
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nstate:\n  - " + declaration + "\n");
        _root.File("teams/sample/sample.workload", "template: example.com/base:1\n");
        var resolver = new SquadResolver(new AboxConfig());
        var build = new BuildService(
            resolver,
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            new PersonalConfigurations(_paths),
            _store,
            LocalWorkloadTest.Images(),
            _sbx,
            _console
        );
        var name = SandboxName.For(_root.Path);
        var hostPath = Path.Combine(_paths.StateDirectory, name, relativePath);

        // Act
        await build.BuildAsync(
            new BuildRequest(
                [resolver.ResolveTeam(_root.Path, "sample")],
                _root.Path,
                "latest",
                Verify: false
            ),
            CancellationToken.None
        );
        var first = await _create.CreateAsync(Request(), CancellationToken.None);
        var saved = Path.Combine(hostPath, "session.txt");
        File.WriteAllText(saved, "keep this session");
        var recreated = await _create.CreateAsync(
            Request() with
            {
                ExistingSandbox = new SandboxInfo(name, "running", DateTimeOffset.UtcNow),
            },
            CancellationToken.None
        );

        // Assert
        first.Should().Be(0);
        recreated.Should().Be(0);
        File.ReadAllText(saved).Should().Be("keep this session");
        var manifest = _store.Load("sample", "ac", "latest")!;
        manifest
            .State.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new ManifestState { SandboxPath = sandboxPath, RelativePath = relativePath }
            );
        File.ReadAllText(Path.Combine(manifest.StateMixinDirectory!, "state.yaml"))
            .Should()
            .Contain("path: " + sandboxPath);
        await _sbx.Received(2)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.Mounts.Count == 1
                    && request.Mounts[0].SandboxPath == sandboxPath
                    && request.Mounts[0].HostPath == hostPath
                ),
                Arg.Any<CancellationToken>()
            );
        await _sbx.Received(1).RemoveSandboxAsync(name, Arg.Any<CancellationToken>());
    }

    private SandboxCreationRequest Request() =>
        new(
            new SquadResolver.ResolvedTeam("sample", "teams/sample", ["ac"]),
            "ac",
            _root.Path,
            "latest",
            FolderConfig: null,
            AssumeYes: true,
            Local: true
        );

    private BuildManifest SaveBuild(List<ManifestState> state)
    {
        var manifest = new BuildManifest
        {
            Squad = "ac",
            Tag = "latest",
            Reference = ImageName.OfTeam("sample", "latest"),
            LocalWorkload = LocalWorkloadTest.Artifact(_root),
            State = state,
            Mcp = [new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" }],
        };
        _store.Save(manifest);
        return manifest;
    }

    public void Dispose() => _root.Dispose();
}
