using Abox.Configuration;
using Abox.Features.Build;
using Abox.Features.Prune;
using Abox.Features.Run;
using Abox.Features.Secrets;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sandboxes;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class CreateServiceTests
{
    private readonly TempDirectory _root = new();
    private readonly TestConsole _console = new();
    private readonly ISbxClient _sbx = Substitute.For<ISbxClient>();
    private readonly IPublishedSquadSource _published = PublishedSquadTest.Source();
    private readonly AboxPaths _paths;
    private readonly BuildStore _store;
    private readonly SandboxRecordStore _records;
    private readonly SandboxCreator _service;

    public CreateServiceTests()
    {
        _paths = AboxPaths.For(
            Path.Combine(_root.Path, "config"),
            Path.Combine(_root.Path, "state")
        );
        _store = new BuildStore(_paths);
        _records = new SandboxRecordStore(_paths);
        _service = new SandboxCreator(
            _records,
            _paths,
            _sbx,
            _console,
            new LocalSquadSource(_store, LocalWorkloadTest.Images(), _console),
            new RemoteSquadSource(new AboxConfig(), _sbx, _published)
        );

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
        _published.ReceivedCalls().Should().BeEmpty("--local must not pull registry metadata");
        await _sbx.DidNotReceive()
            .EnsureKitSourceAllowedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource
                        == _store.Load("ac", "latest")!.LocalWorkload!.RuntimeKitDirectory
                    && request.HostDockerContext == "test-context"
                    && request.PullPolicy == "never"
                    && request.Name == SandboxName.For(_root.Path)
                    && request.Mixins.Contains("/kits/pi-config")
                    && request.Mixins.Contains("/kits/state")
                    && request.KitArguments["mode"] == "team"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task LocalSquadNamesUseTheCanonicalCaseForBuildsAndRecords()
    {
        SaveBuild();

        var exitCode = await _service.CreateAsync(
            Request() with
            {
                Squad = "AC",
            },
            CancellationToken.None
        );

        exitCode.Should().Be(0);
        _records.Load(SandboxName.For(_root.Path))!.Squad.Should().Be("ac");
    }

    [Theory]
    [InlineData("team")]
    [InlineData("different")]
    public async Task SharedKitArgumentsMustHaveTheSameValue(string value)
    {
        SaveBuild();
        var manifest = _store.Load("ac", "latest")!;
        manifest.Kits.Add(
            new ManifestKit
            {
                Name = "other-kit",
                Reference = "/kits/other",
                Arguments = new Dictionary<string, string> { ["mode"] = value },
            }
        );
        _store.Save(manifest);

        if (value == "team")
        {
            (await _service.CreateAsync(Request(), CancellationToken.None)).Should().Be(0);
            await _sbx.Received(1)
                .CreateAsync(
                    Arg.Is<SbxCreateRequest>(request =>
                        request.KitArguments.Count == 1 && request.KitArguments["mode"] == "team"
                    ),
                    Arg.Any<CancellationToken>()
                );
        }
        else
        {
            var action = () => _service.CreateAsync(Request(), CancellationToken.None);
            (await action.Should().ThrowAsync<AboxException>()).WithMessage(
                "*mode*pi-config*other-kit*"
            );
            await _sbx.DidNotReceive()
                .FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await _sbx.DidNotReceive()
                .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData("mtiae-docker-release")]
    [InlineData("team-docker-release")]
    public async Task PublishedSetsArePulledFromTheConfiguredRepository(string repository)
    {
        SaveBuild(mcp: [new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" }]);
        var service = new SandboxCreator(
            _records,
            _paths,
            _sbx,
            _console,
            new LocalSquadSource(_store, LocalWorkloadTest.Images(), _console),
            new RemoteSquadSource(
                new AboxConfig { PullRepository = repository },
                _sbx,
                PublishedSquadTest.Source([
                    new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" },
                ])
            )
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
            .EnsureKitSourceAllowedAsync(
                $"rwest.jfrog.io/{repository}/",
                Arg.Any<CancellationToken>()
            );
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource
                        == $"rwest.jfrog.io/{repository}/agentbox-rwest-sample/ac@{PublishedSquadTest.Digest}"
                    && request.PullPolicy == "always"
                    && request.HostDockerContext == null
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
            .Be(
                $"rwest.jfrog.io/{repository}/agentbox-rwest-sample/ac@{PublishedSquadTest.Digest}"
            );
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
        var service = new SandboxCreator(
            _records,
            _paths,
            _sbx,
            _console,
            new LocalSquadSource(_store, LocalWorkloadTest.Images(), _console),
            new RemoteSquadSource(new AboxConfig { PullRepository = repository }, _sbx, _published)
        );
        var request = new SandboxCreationRequest(
            new SquadResolver.ResolvedTeam(team, "teams/sample", [squad]),
            squad,
            _root.Path,
            tag
        );

        var action = () => service.CreateAsync(request, CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage($"*{error}*");
        await _sbx.DidNotReceive()
            .EnsureKitSourceAllowedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _published.ReceivedCalls().Should().BeEmpty();
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
    public async Task PullingIgnoresLocalMetadataEvenWhenItBelongsToAnotherTeam(string team)
    {
        SaveBuild(reference: team == "" ? "" : ImageName.OfTeam(team, "latest"));

        (await _service.CreateAsync(Request() with { Local = false }, CancellationToken.None))
            .Should()
            .Be(0);
        await _published
            .Received(1)
            .LoadAsync(Arg.Any<string>(), "sample", "ac", "latest", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOnlySquadOfTheTeamIsUsedWithoutAsking()
    {
        SaveBuild();
        var request = new SandboxCreationRequest(
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
        var request = new SandboxCreationRequest(
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecliningReplacementDoesNotRegisterMcpOrCreateStateDirectories(bool local)
    {
        var mcp = new List<ManifestMcp>
        {
            new() { Name = "elastic", Url = "https://example.com/mcp" },
        };
        SaveBuild(mcp);
        var service = new SandboxCreator(
            _records,
            _paths,
            _sbx,
            _console,
            new LocalSquadSource(_store, LocalWorkloadTest.Images(), _console),
            new RemoteSquadSource(new AboxConfig(), _sbx, PublishedSquadTest.Source(mcp))
        );
        _console.Answer = false;
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.UtcNow));

        var action = () =>
            service.CreateAsync(Request() with { Local = local }, CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*not replaced*");
        await _sbx.DidNotReceive()
            .AddMcpServerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Directory.Exists(Path.Combine(_paths.StateDirectory, name)).Should().BeFalse();
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task McpRegistrationFailureAfterConsentDoesNotRemoveTheExistingSandbox()
    {
        SaveBuild([new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" }]);
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.UtcNow));
        _sbx.AddMcpServerAsync("elastic", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _console.AllText.Should().Contain("Replace the sandbox?");
                return new SbxOutcome(1, "registration failed");
            });

        var action = () => _service.CreateAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*could not be registered*"
        );
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Directory.Exists(Path.Combine(_paths.StateDirectory, name)).Should().BeFalse();
    }

    [Fact]
    public async Task AllMcpRegistrationsAreValidatedReadOnlyBeforeAskingForReplacement()
    {
        SaveBuild([
            new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" },
            new ManifestMcp { Name = "missing" },
        ]);
        var action = () => _service.CreateAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*missing*not registered*");
        await _sbx.DidNotReceive()
            .FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .AddMcpServerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        Directory
            .Exists(Path.Combine(_paths.StateDirectory, SandboxName.For(_root.Path)))
            .Should()
            .BeFalse();
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

    private SandboxCreationRequest Request() =>
        new(
            new SquadResolver.ResolvedTeam("sample", "teams/sample", ["ac"]),
            "ac",
            _root.Path,
            "latest",
            Local: true
        );

    [Fact]
    public async Task OldLocalManifestsAreRejectedWithoutTouchingAnExistingSandbox()
    {
        SaveBuild();
        var build = _store.Load("ac", "latest")!;
        build.LocalWorkload = null;
        _store.Save(build);

        var action = () => _service.CreateAsync(Request(), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*no prebuilt local workload*abox build*without --metadata-only*"
        );
        await _sbx.DidNotReceive()
            .FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ChangedThinKitFailsBeforeReplacementRatherThanBuildingSources()
    {
        SaveBuild();
        var build = _store.Load("ac", "latest")!;
        File.AppendAllText(
            Path.Combine(build.LocalWorkload!.RuntimeKitDirectory, "workload.dockerfile"),
            "RUN install-again\n"
        );

        var action = () =>
            _service.CreateAsync(Request() with { AssumeYes = true }, CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*runtime kit changed*");
        await _sbx.DidNotReceive()
            .FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoteCreationConsumesOnlyThePublishedSetEvenWithoutALocalImage()
    {
        SaveBuild();
        var build = _store.Load("ac", "latest")!;
        Directory.Delete(build.LocalWorkload!.RuntimeKitDirectory, recursive: true);
        build.LocalWorkload = null;
        _store.Save(build);
        var runner = LocalWorkloadTest.Runner();
        var service = new SandboxCreator(
            _records,
            _paths,
            _sbx,
            _console,
            new LocalSquadSource(_store, LocalWorkloadTest.Images(runner), _console),
            new RemoteSquadSource(new AboxConfig(), _sbx, _published)
        );

        var exitCode = await service.CreateAsync(
            Request() with
            {
                Local = false,
            },
            CancellationToken.None
        );

        exitCode.Should().Be(0);
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<Abox.Infrastructure.ProcessRequest>(), Arg.Any<CancellationToken>());
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource
                        == $"rwest.jfrog.io/mtiae-docker-release/agentbox-rwest-sample/ac@{PublishedSquadTest.Digest}"
                    && request.Mixins.Count == 0
                    && request.KitArguments.Count == 0
                    && request.HostDockerContext == null
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task MetadataOnlyPreparationDoesNotEnableLocalCreateOrSupplyRemoteMetadata()
    {
        _root.File(
            "teams/sample/ac.abox",
            "workload: sample\nkits:\n  - name: probe\n    args:\n      mode: team\nstate: [~/.probe-state]\nmcp: [elastic]\n"
        );
        _root.File("teams/sample/sample.workload", "template: example.com/base:1\n");
        _root.File(
            "kits/probe/probe.yaml",
            "schemaVersion: \"3\"\nkind: mixin\nargs:\n  mode:\n    required: true\n    env: MODE\n"
        );
        var resolver = new SquadResolver(new AboxConfig());
        var team = resolver.ResolveTeam(_root.Path, "sample");
        var runner = Substitute.For<IProcessRunner>();
        var programs = Substitute.For<IHostPrograms>();
        var images = new LocalWorkloadImages(programs, new OciRegistry(runner), _console);
        var build = new BuildService(
            resolver,
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            new PersonalConfigurations(_paths),
            _store,
            images,
            _sbx,
            _console
        );
        await build.BuildAsync(
            new BuildRequest([team], _root.Path, "latest", MetadataOnly: true),
            CancellationToken.None
        );
        _store.Load("ac", "latest")!.LocalWorkload.Should().BeNull();
        _sbx.ListMcpServersAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)["elastic"]);
        var service = new SandboxCreator(
            _records,
            _paths,
            _sbx,
            _console,
            new LocalSquadSource(_store, images, _console),
            new RemoteSquadSource(
                new AboxConfig(),
                _sbx,
                PublishedSquadTest.Source([new ManifestMcp { Name = "elastic" }])
            )
        );

        var exitCode = await service.CreateAsync(
            Request() with
            {
                Team = team,
                Local = false,
            },
            CancellationToken.None
        );

        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource
                        == $"rwest.jfrog.io/mtiae-docker-release/agentbox-rwest-sample/ac@{PublishedSquadTest.Digest}"
                    && request.Mixins.Count == 0
                    && request.KitArguments.Count == 0
                    && request.HostDockerContext == null
                    && request.Mounts.Count == 1
                    && request.StaticMcpServers.SequenceEqual(new[] { "elastic" })
                ),
                Arg.Any<CancellationToken>()
            );
        runner.ReceivedCalls().Should().BeEmpty();
        programs.ReceivedCalls().Should().BeEmpty();
        _sbx.ClearReceivedCalls();

        var local = () =>
            service.CreateAsync(Request() with { Team = team }, CancellationToken.None);

        (await local.Should().ThrowAsync<AboxException>()).WithMessage(
            "*no prebuilt local workload*abox build --team sample --tag latest*without --metadata-only*"
        );
        _sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task RemoteCreateNeedsNoLocalBuildAndUsesPublishedState()
    {
        (await _service.CreateAsync(Request() with { Local = false }, CancellationToken.None))
            .Should()
            .Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.Mounts[0].SandboxPath == "/home/agent/.published"
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task UnreadablePublishedMetadataCannotDeleteAnExistingSandboxEvenWithYes()
    {
        SaveBuild();
        _published
            .LoadAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<PublishedSquad>(_ =>
                throw new AboxException("Invalid bundled metadata; republish the set.")
            );
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.UtcNow));

        var action = () =>
            _service.CreateAsync(
                Request() with
                {
                    Local = false,
                    AssumeYes = true,
                },
                CancellationToken.None
            );

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*republish*");
        _sbx.ReceivedCalls()
            .Should()
            .ContainSingle()
            .Which.GetMethodInfo()
            .Name.Should()
            .Be(
                nameof(ISbxClient.EnsureKitSourceAllowedAsync),
                "only repository trust may be updated before published metadata is validated"
            );
        _records.Load(name).Should().BeNull();
    }

    [Fact]
    public async Task RemoteRepositoryTrustIsEnsuredBeforeRegistryWorkAndSandboxReplacement()
    {
        var calls = new List<string>();
        _sbx.When(client =>
                client.EnsureKitSourceAllowedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            )
            .Do(_ => calls.Add("trust"));
        _published
            .When(source =>
                source.LoadAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(_ => calls.Add("registry"));
        _sbx.When(client =>
                client.RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            )
            .Do(_ => calls.Add("remove"));
        _sbx.When(client =>
                client.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            )
            .Do(_ => calls.Add("create"));
        _sbx.FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo("existing", "running", DateTimeOffset.UtcNow));

        await _service.CreateAsync(
            Request() with
            {
                Local = false,
                AssumeYes = true,
            },
            CancellationToken.None
        );

        calls.Should().Equal("trust", "registry", "remove", "create");
    }

    [Fact]
    public async Task DeniedRepositoryTrustCannotPullOrRemoveAnExistingSandboxEvenWithYes()
    {
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.UtcNow));
        _sbx.EnsureKitSourceAllowedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
                throw new AboxException("Administrator policy denies kit.allowedSources update.")
            );

        var action = () =>
            _service.CreateAsync(
                Request() with
                {
                    Local = false,
                    AssumeYes = true,
                },
                CancellationToken.None
            );

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*Administrator policy*kit.allowedSources*"
        );
        _published.ReceivedCalls().Should().BeEmpty();
        await _sbx.DidNotReceive()
            .FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
        _records.Load(name).Should().BeNull();
        Directory.Exists(Path.Combine(_paths.StateDirectory, name)).Should().BeFalse();
    }

    [Fact]
    public async Task MissingPublishedMcpRegistrationFailsBeforeSandboxReplacement()
    {
        _published
            .LoadAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new PublishedSquad(
                    "pinned-set",
                    new BuildManifest { Mcp = [new ManifestMcp { Name = "missing" }] }
                )
            );

        var action = () =>
            _service.CreateAsync(
                Request() with
                {
                    Local = false,
                    AssumeYes = true,
                },
                CancellationToken.None
            );

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*not registered*");
        await _sbx.DidNotReceive()
            .FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnusablePublishedStateDirectoryFailsBeforeSandboxReplacement()
    {
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.UtcNow));
        _root.File($"state/{name}/.published", "a file blocks the state directory");
        var action = () =>
            _service.CreateAsync(
                Request() with
                {
                    Local = false,
                    AssumeYes = true,
                },
                CancellationToken.None
            );

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*state director*not replaced*"
        );
        await _sbx.Received(1).FindSandboxAsync(name, Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private void SaveBuild(List<ManifestMcp>? mcp = null, string? reference = null)
    {
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = reference ?? ImageName.OfTeam("sample", "latest"),
                WorkloadKitDirectory = "/kits/workload",
                LocalWorkload = LocalWorkloadTest.Artifact(_root),
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
