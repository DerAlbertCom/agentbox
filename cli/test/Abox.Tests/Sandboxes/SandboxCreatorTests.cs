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
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

public sealed class SandboxCreatorTests
{
    private static readonly SquadBuildIdentity Identity = new("sample", "ac", "latest");

    private SandboxCreator Creator(
        RemoteSquadSource? remote = null,
        LocalWorkloadImages? images = null
    ) => _fixture.Creator(remote, images);

    private readonly SandboxCreatorFixture _fixture = new();
    private TempDirectory _root => _fixture.Root;
    private TestConsole _console => _fixture.Console;
    private ISbxClient _sbx => _fixture.Sbx;
    private IPublishedSquadSource _published => _fixture.Published;
    private AboxPaths _paths => _fixture.Paths;
    private BuildStore _store => _fixture.Store;
    private SandboxRecordStore _records => _fixture.Records;
    private SandboxCreator _service => _fixture.Service;

    [Fact]
    public async Task ASandboxIsCreatedFromTheLocalBuild()
    {
        // Arrange
        SaveBuild();

        // Act
        var exitCode = await _service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        _published.ReceivedCalls().Should().BeEmpty("--local must not pull registry metadata");
        await _sbx.DidNotReceive()
            .EnsureKitSourceAllowedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource
                        == _store
                            .Load(new SquadBuildIdentity("sample", "ac", "latest"))!
                            .LocalWorkload!.RuntimeKitDirectory
                    && request.HostDockerContext == "test-context"
                    && request.PullPolicy == PullPolicy.Never
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
        // Arrange
        SaveBuild();

        // Act
        var exitCode = await _service.CreateAsync(
            Request() with
            {
                Squad = "AC",
            },
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        _records.Load(SandboxName.For(_root.Path))!.Squad.Should().Be("ac");
    }

    [Theory]
    [InlineData("team")]
    [InlineData("different")]
    public async Task SharedKitArgumentsMustHaveTheSameValue(string value)
    {
        // Arrange
        SaveBuild();
        var manifest = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
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
            // Act
            var exitCode = await _service.CreateAsync(Request(), CancellationToken.None);

            // Assert
            exitCode.Should().Be(0);
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
            // Act
            var action = () => _service.CreateAsync(Request(), CancellationToken.None);

            // Assert
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
        // Arrange
        SaveBuild(mcp: [new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" }]);
        var service = Creator(
            new RemoteSquadSource(
                new AboxConfig { PullRepository = repository },
                _sbx,
                PublishedSquadFactory.Source([
                    new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" },
                ])
            )
        );

        // Act
        var exitCode = await service.CreateAsync(
            Request() with
            {
                Local = false,
            },
            CancellationToken.None
        );

        // Assert
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
                        == $"rwest.jfrog.io/{repository}/agentbox-rwest-sample/ac@{PublishedSquadFactory.Digest}"
                    && request.PullPolicy == PullPolicy.Always
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
                $"rwest.jfrog.io/{repository}/agentbox-rwest-sample/ac@{PublishedSquadFactory.Digest}"
            );
    }

    [Fact]
    public async Task PullFailuresAreReportedWithoutFallingBackToLocalKits()
    {
        // Arrange
        SaveBuild();
        _sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(1, "registry access denied"));

        // Act
        var action = () =>
            _service.CreateAsync(Request() with { Local = false }, CancellationToken.None);

        // Assert
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
        // Arrange
        var service = Creator(
            new RemoteSquadSource(new AboxConfig { PullRepository = repository }, _sbx, _published)
        );
        var request = new SandboxCreationRequest(
            new SquadResolver.ResolvedTeam(team, "teams/sample", [squad]),
            squad,
            _root.Path,
            tag,
            FolderConfig: null
        );

        // Act
        var action = () => service.CreateAsync(request, CancellationToken.None);

        // Assert
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
        // Arrange
        _root.File(
            "state/builds/ac/latest/build.json",
            System.Text.Json.JsonSerializer.Serialize(
                new BuildManifest
                {
                    Squad = "ac",
                    Tag = "latest",
                    Reference = team == "" ? "" : ImageName.OfTeam(team, "latest"),
                }
            )
        );

        // Act
        var exitCode = await _service.CreateAsync(
            Request() with
            {
                Local = false,
            },
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        await _published
            .Received(1)
            .LoadAsync(Arg.Any<string>(), Identity, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheOnlySquadOfTheTeamIsUsedWithoutAsking()
    {
        // Arrange
        SaveBuild();
        var request = new SandboxCreationRequest(
            new SquadResolver.ResolvedTeam("sample", "teams/sample", ["ac"]),
            null,
            _root.Path,
            "latest",
            FolderConfig: null,
            Local: true
        );

        // Act
        var exitCode = await _service.CreateAsync(request, CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        _console.AllText.Should().NotContain("Which squad");
        _records.Load(SandboxName.For(_root.Path))!.Squad.Should().Be("ac");
    }

    [Fact]
    public async Task TheSquadIsAskedForWhenTheTeamHasMoreThanOne()
    {
        // Arrange
        SaveBuild();
        _console.Picks.Add(0);
        var request = new SandboxCreationRequest(
            new SquadResolver.ResolvedTeam("sample", "teams/sample", ["ac", "bd"]),
            null,
            _root.Path,
            "latest",
            FolderConfig: null,
            Local: true
        );

        // Act
        var exitCode = await _service.CreateAsync(request, CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        _console.AllText.Should().Contain("Which squad of the team 'sample'");
        _records.Load(SandboxName.For(_root.Path))!.Squad.Should().Be("ac");
    }

    [Fact]
    public async Task ABuildThatIsNotThereIsAnError()
    {
        // Arrange: no local build has been saved in the fixture's store.

        // Act
        var action = () => _service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("abox build --team sample");
    }

    [Fact]
    public async Task AnExistingSandboxIsReplacedAfterConfirmation()
    {
        // Arrange
        SaveBuild();
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));

        // Act
        var exitCode = await _service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        await _sbx.Received(1).RemoveSandboxAsync(name, Arg.Any<CancellationToken>());
        _console.AllText.Should().Contain("already exists");
    }

    [Fact]
    public async Task AnExistingSandboxIsKeptWhenTheAnswerIsNo()
    {
        // Arrange
        SaveBuild();
        _console.Answer = false;
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.Now));

        // Act
        var action = () => _service.CreateAsync(Request(), CancellationToken.None);

        // Assert
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
        // Arrange
        var mcp = new List<ManifestMcp>
        {
            new() { Name = "elastic", Url = "https://example.com/mcp" },
        };
        SaveBuild(mcp);
        var service = Creator(
            new RemoteSquadSource(new AboxConfig(), _sbx, PublishedSquadFactory.Source(mcp))
        );
        _console.Answer = false;
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.UtcNow));

        // Act
        var action = () =>
            service.CreateAsync(Request() with { Local = local }, CancellationToken.None);

        // Assert
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
        // Arrange
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

        // Act
        var action = () => _service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*could not be registered*"
        );
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Directory.Exists(Path.Combine(_paths.StateDirectory, name)).Should().BeFalse();
    }

    [Theory]
    [InlineData("missing", null, "*missing*not registered*")]
    [InlineData("elastic,other", null, "*mcp.name*not valid*")]
    [InlineData("other", "file:///local", "*mcp.url*not valid*")]
    [InlineData("other", "https://user:password@example.com/mcp", "*mcp.url*not valid*")]
    public async Task AllMcpRegistrationsAreValidatedReadOnlyBeforeAskingForReplacement(
        string name,
        string? url,
        string error
    )
    {
        // Arrange
        SaveBuild([
            new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" },
            new ManifestMcp { Name = name, Url = url },
        ]);

        // Act
        var action = () => _service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>())
            .WithMessage(error)
            .Which.Message.Should()
            .NotContain("password");
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
        // Arrange
        SaveBuild();

        // Act
        await _service.CreateAsync(Request(), CancellationToken.None);

        // Assert
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
        // Arrange
        SaveBuild(mcp: [new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" }]);

        // Act
        await _service.CreateAsync(Request(), CancellationToken.None);

        // Assert
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
        // Arrange
        SaveBuild(mcp: [new ManifestMcp { Name = "elastic" }]);

        // Act
        var action = () => _service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        var exception = await action.Should().ThrowAsync<AboxException>();

        exception.Which.Message.Should().Contain("not registered");
    }

    [Fact]
    public async Task TheRecordOfTheSandboxIsKept()
    {
        // Arrange
        SaveBuild();

        // Act
        await _service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        var record = _records.Load(SandboxName.For(_root.Path));
        record.Should().NotBeNull();
        record!.Squad.Should().Be("ac");
        record.Tag.Should().Be("latest");
    }

    private SandboxCreationRequest Request() => _fixture.Request();

    [Fact]
    public async Task OldLocalManifestsAreRejectedWithoutTouchingAnExistingSandbox()
    {
        // Arrange
        SaveBuild();
        var build = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.LocalWorkload = null;
        _store.Save(build);

        // Act
        var action = () => _service.CreateAsync(Request(), CancellationToken.None);

        // Assert
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
        // Arrange
        SaveBuild();
        var build = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        File.AppendAllText(
            Path.Combine(build.LocalWorkload!.RuntimeKitDirectory, "workload.dockerfile"),
            "RUN install-again\n"
        );

        // Act
        var action = () =>
            _service.CreateAsync(Request() with { AssumeYes = true }, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*runtime kit changed*");
        await _sbx.DidNotReceive()
            .FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RemoteCreationConsumesOnlyThePublishedSetEvenWithoutALocalImage()
    {
        // Arrange
        SaveBuild();
        var build = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        Directory.Delete(build.LocalWorkload!.RuntimeKitDirectory, recursive: true);
        build.LocalWorkload = null;
        _store.Save(build);
        var runner = LocalWorkloadFactory.Runner();
        var service = Creator(images: LocalWorkloadFactory.Images(runner));

        // Act
        var exitCode = await service.CreateAsync(
            Request() with
            {
                Local = false,
            },
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        await runner
            .DidNotReceive()
            .RunAsync(Arg.Any<Abox.Infrastructure.ProcessRequest>(), Arg.Any<CancellationToken>());
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource
                        == $"rwest.jfrog.io/mtiae-docker-release/agentbox-rwest-sample/ac@{PublishedSquadFactory.Digest}"
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
        // Arrange
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
        var build = TestServiceGraphs.Build(
            resolver,
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
        _store.Load(Identity)!.LocalWorkload.Should().BeNull();
        _sbx.ListMcpServersAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)["elastic"]);
        var service = Creator(
            new RemoteSquadSource(
                new AboxConfig(),
                _sbx,
                PublishedSquadFactory.Source([new ManifestMcp { Name = "elastic" }])
            ),
            images
        );

        // Act
        var exitCode = await service.CreateAsync(
            Request() with
            {
                Team = team,
                Local = false,
            },
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource
                        == $"rwest.jfrog.io/mtiae-docker-release/agentbox-rwest-sample/ac@{PublishedSquadFactory.Digest}"
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

        // Arrange
        _sbx.ClearReceivedCalls();

        // Act
        var local = () =>
            service.CreateAsync(Request() with { Team = team }, CancellationToken.None);

        // Assert
        (await local.Should().ThrowAsync<AboxException>()).WithMessage(
            "*no prebuilt local workload*abox build --team sample --tag latest*without --metadata-only*"
        );
        _sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task RemoteCreateNeedsNoLocalBuildAndUsesPublishedState()
    {
        // Arrange: the fixture has published metadata but no local build.

        // Act
        var exitCode = await _service.CreateAsync(
            Request() with
            {
                Local = false,
            },
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
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
        // Arrange
        SaveBuild();
        _published
            .LoadAsync(
                Arg.Any<string>(),
                Arg.Any<SquadBuildIdentity>(),
                Arg.Any<CancellationToken>()
            )
            .Returns<PublishedSquad>(_ =>
                throw new AboxException("Invalid bundled metadata; republish the set.")
            );
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.UtcNow));

        // Act
        var action = () =>
            _service.CreateAsync(
                Request() with
                {
                    Local = false,
                    AssumeYes = true,
                },
                CancellationToken.None
            );

        // Assert
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
        // Arrange
        var calls = new List<string>();
        _sbx.When(client =>
                client.EnsureKitSourceAllowedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            )
            .Do(_ => calls.Add("trust"));
        _published
            .When(source =>
                source.LoadAsync(
                    Arg.Any<string>(),
                    Arg.Any<SquadBuildIdentity>(),
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

        // Act
        await _service.CreateAsync(
            Request() with
            {
                Local = false,
                AssumeYes = true,
            },
            CancellationToken.None
        );

        // Assert
        calls.Should().Equal("trust", "registry", "remove", "create");
    }

    [Fact]
    public async Task DeniedRepositoryTrustCannotPullOrRemoveAnExistingSandboxEvenWithYes()
    {
        // Arrange
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.UtcNow));
        _sbx.EnsureKitSourceAllowedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
                throw new AboxException("Administrator policy denies kit.allowedSources update.")
            );

        // Act
        var action = () =>
            _service.CreateAsync(
                Request() with
                {
                    Local = false,
                    AssumeYes = true,
                },
                CancellationToken.None
            );

        // Assert
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
        // Arrange
        _published
            .LoadAsync(
                Arg.Any<string>(),
                Arg.Any<SquadBuildIdentity>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new PublishedSquad(
                    "pinned-set",
                    new BuildManifest { Mcp = [new ManifestMcp { Name = "missing" }] }
                )
            );

        // Act
        var action = () =>
            _service.CreateAsync(
                Request() with
                {
                    Local = false,
                    AssumeYes = true,
                },
                CancellationToken.None
            );

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*not registered*");
        await _sbx.DidNotReceive()
            .FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UnusablePublishedStateDirectoryFailsBeforeSandboxReplacement()
    {
        // Arrange
        var name = SandboxName.For(_root.Path);
        _sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.UtcNow));
        _root.File($"state/{name}/.published", "a file blocks the state directory");

        // Act
        var action = () =>
            _service.CreateAsync(
                Request() with
                {
                    Local = false,
                    AssumeYes = true,
                },
                CancellationToken.None
            );

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*state director*not replaced*"
        );
        await _sbx.Received(1).FindSandboxAsync(name, Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private void SaveBuild(List<ManifestMcp>? mcp = null, string? reference = null) =>
        _fixture.SaveBuild(mcp, reference);
}
