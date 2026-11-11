using System.Text.Json;
using Abox.Configuration;
using Abox.Features.Publish;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using NSubstitute;

namespace Abox.Tests;

public sealed class PublishServiceTests
{
    private readonly TempDirectory _root = new();
    private readonly BuildStore _store;
    private readonly IProcessRunner _runner = Substitute.For<IProcessRunner>();
    private readonly IHostPrograms _programs = Substitute.For<IHostPrograms>();
    private readonly TestConsole _console = new();
    private readonly List<ProcessRequest> _calls = [];
    private readonly List<string> _descriptors = [];
    private readonly List<string> _configurations = [];
    private readonly PublishService _service;

    public PublishServiceTests()
    {
        _store = new BuildStore(AboxPaths.For(_root.Path, Path.Combine(_root.Path, "state")));
        _service = new PublishService(_store, _programs, new OciRegistry(_runner), _console);
        _programs.Exists("docker").Returns(true);
        _runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                _calls.Add(request);
                var arguments = request.Arguments.ToList();
                if (arguments.Contains("--file"))
                {
                    _descriptors.Add(File.ReadAllText(arguments[arguments.IndexOf("--file") + 1]));
                    var config = Path.Combine(arguments.Last(), "squad.json");
                    if (File.Exists(config))
                    {
                        _configurations.Add(File.ReadAllText(config));
                    }
                }

                return PublishTestProcess.Run(request);
            });
    }

    [Fact]
    public async Task PersonalConfigurationKitsCannotBePublished()
    {
        _root.File("teams/sample/ac.abox", "workload: sample\n");
        _root.File("teams/sample/sample.workload", "template: example.com/base:1\n");
        _root.File("kits/private/private.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        var configDirectory = Path.Combine(_root.Path, "config");
        _root.File("config/personal.abox", "kits:\n  - private\n");
        var build = new Abox.Features.Build.BuildService(
            new SquadResolver(new AboxConfig()),
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            new PersonalConfigurations(AboxPaths.For(configDirectory, _root.Path)),
            _store,
            LocalWorkloadTest.Images(),
            Substitute.For<Abox.Sbx.ISbxClient>(),
            _console
        );
        await build.BuildAsync(
            new Abox.Features.Build.BuildRequest(
                Request("ac").Teams,
                _root.Path,
                "latest",
                Verify: false
            ),
            CancellationToken.None
        );
        File.Delete(Path.Combine(configDirectory, "personal.abox"));

        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*personal configuration*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task LocalAndRegistryKitsKeepTheirArgumentsAndStateIsIncluded()
    {
        SaveBuild("ac");
        var build = _store.Load("ac", "latest")!;
        var local = Path.Combine(_root.Path, "local kit");
        Directory.CreateDirectory(local);
        File.WriteAllText(
            Path.Combine(local, "spec.yaml"),
            "schemaVersion: \"3\"\nkind: mixin\nargs:\n  url:\n    env: SERVICE_URL\n"
        );
        build.Kits =
        [
            new ManifestKit
            {
                Name = "local",
                Reference = local,
                IsLocal = true,
                Arguments = new() { ["url"] = "https://example.com/a?x=hello world&y=1" },
            },
            new ManifestKit
            {
                Name = "remote",
                Reference = "ghcr.io/example/tool:1",
                Arguments = new() { ["message"] = "quoted: \"hello\"\nnext line" },
            },
        ];
        build.StateMixinDirectory = local;
        build.State.Add(
            new ManifestState { SandboxPath = "/home/agent/.state", RelativePath = ".state" }
        );
        build.Mcp.Add(new ManifestMcp { Name = "elastic", Url = "https://example.com/mcp" });
        SaveVerifiedBuild(build);

        await _service.PublishAsync(
            Request("ac") with
            {
                Builder = "custom-builder",
            },
            CancellationToken.None
        );

        var pushes = _calls.Where(call => call.Arguments.Contains("--push")).ToList();
        pushes.Should().HaveCount(5);
        pushes.Should().OnlyContain(call => call.Arguments.Contains("custom-builder"));
        pushes.Should().OnlyContain(call => !call.Arguments.Contains("--build-arg"));
        var set = YamlReader.Parse(_descriptors.Last(), "set");
        var kits = set.GetEntries("kits");
        kits.Should().HaveCount(5);
        kits[1].GetEntry("args")!.GetString("url").Should().Be(build.Kits[0].Arguments["url"]);
        kits[2].GetString("ref").Should().Be("ghcr.io/example/tool:1");
        kits[2]
            .GetEntry("args")!
            .GetString("message")
            .Should()
            .Be(build.Kits[1].Arguments["message"]);
        using var config = JsonDocument.Parse(_configurations.Single());
        config.RootElement.GetProperty("SchemaVersion").GetInt32().Should().Be(1);
        config
            .RootElement.GetProperty("Mcp")[0]
            .GetProperty("Name")
            .GetString()
            .Should()
            .Be("elastic");
        config
            .RootElement.GetProperty("State")[0]
            .GetProperty("SandboxPath")
            .GetString()
            .Should()
            .Be("/home/agent/.state");
        _configurations.Single().Should().NotContain(_root.Path);
    }

    [Fact]
    public async Task EachSquadPublishesToItsOwnWorkloadRepository()
    {
        SaveBuild("ac", "first-images-local");
        SaveBuild("bd", "second-images-local");

        await _service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        var tags = _calls
            .Where(call => call.Arguments.Contains("--tag"))
            .Select(call => call.Arguments[call.Arguments.ToList().IndexOf("--tag") + 1])
            .ToList();
        tags.Should().HaveCount(8);
        tags.Take(4)
            .Should()
            .OnlyContain(tag =>
                tag.StartsWith("rwest.jfrog.io/first-images-local/agentbox-rwest-sample/ac")
            );
        tags.Skip(4)
            .Should()
            .OnlyContain(tag =>
                tag.StartsWith("rwest.jfrog.io/second-images-local/agentbox-rwest-sample/bd")
            );
        tags.Should().Contain("rwest.jfrog.io/first-images-local/agentbox-rwest-sample/ac:latest");
        tags.Should().Contain("rwest.jfrog.io/second-images-local/agentbox-rwest-sample/bd:latest");
        foreach (var descriptor in _descriptors.Where(text => text.Contains("\"kind\": \"set\"")))
        {
            descriptor.Should().NotContain("mtiae-developer-container-images-local");
        }
    }

    [Fact]
    public async Task MultipleSquadsDoNotOverwriteEachOther()
    {
        SaveBuild("ac");
        SaveBuild("bd");

        await _service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        _calls
            .SelectMany(call => call.Arguments)
            .Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentbox-rwest-sample/ac:latest"
            );
        _calls
            .SelectMany(call => call.Arguments)
            .Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentbox-rwest-sample/bd:latest"
            );
        _calls
            .SelectMany(call => call.Arguments)
            .Should()
            .NotContain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentbox-rwest-sample:latest"
            );
    }

    [Fact]
    public async Task LegacyBuildsWithoutAPublishRepositoryMustBeRebuilt()
    {
        SaveBuild("ac");
        var build = _store.Load("ac", "latest")!;
        build.PublishRepository = null;
        _store.Save(build);

        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*publishRepository*.workload*abox build --team sample --tag latest --no-personal*"
        );
        _calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../other")]
    [InlineData("team/repository")]
    [InlineData("repository:tag")]
    [InlineData("rwest.jfrog.io/repository")]
    [InlineData("Repository")]
    [InlineData("repository\n")]
    public async Task EveryBuildRepositoryIsValidatedBeforeAnyDockerCall(string repository)
    {
        SaveBuild("ac");
        SaveBuild("bd", repository);

        var action = () => _service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*publishRepository*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task EveryBuildIsCheckedBeforeAnythingIsPublished()
    {
        SaveBuild("ac");

        var action = () => _service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*abox build --team sample --tag latest*"
        );
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ASquadBuildFromAnotherTeamIsNotPublished()
    {
        SaveBuild("ac");
        var build = _store.Load("ac", "latest")!;
        build.Reference = "agentbox-rwest-other:latest";
        _store.Save(build);

        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*not a build of team 'sample'*"
        );
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AMissingBuiltKitDoesNotStartAPublish()
    {
        SaveBuild("ac");
        File.Delete(
            Path.Combine(_store.Load("ac", "latest")!.WorkloadKitDirectory, "workload.yaml")
        );

        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*has no descriptor*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AMissingDockerGetsAnInstallHint()
    {
        SaveBuild("ac");
        _programs.Exists("docker").Returns(false);

        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*Docker with the Buildx plugin*"
        );
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedPushDoesNotMoveTheSetTagAndGetsAnAuthenticationHint()
    {
        SaveBuild("ac");
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--push")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(1, string.Empty, "unauthorized"));

        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        var exception = (await action.Should().ThrowAsync<AboxException>()).Which;
        exception
            .Message.Should()
            .Contain("docker login rwest.jfrog.io")
            .And.Contain("unauthorized");
        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--push")),
                Arg.Any<CancellationToken>()
            );
        _console.AllText.Should().NotContain("Published squad");
        _calls
            .Where(call => call.Arguments.Contains("--push"))
            .Should()
            .OnlyContain(call => !Directory.Exists(call.Arguments.Last()));
    }

    [Fact]
    public async Task ComponentTagsAreUniqueAcrossPublishes()
    {
        SaveBuild("ac");
        await _service.PublishAsync(Request("ac"), CancellationToken.None);
        var firstSet = YamlReader.Parse(_descriptors.Last(), "first set");

        await _service.PublishAsync(Request("ac"), CancellationToken.None);

        var secondSet = YamlReader.Parse(_descriptors.Last(), "second set");
        firstSet
            .GetEntries("kits")[0]
            .GetString("ref")
            .Should()
            .NotBe(secondSet.GetEntries("kits")[0].GetString("ref"));
    }

    [Fact]
    public async Task TheChosenTagIsUsedForTheLocalBuildAndThePublishedSet()
    {
        SaveBuild("ac");
        var build = _store.Load("ac", "latest")!;
        build.Tag = "release-1";
        build.Reference = "agentbox-rwest-sample:release-1";
        _store.Save(build);

        await _service.PublishAsync(
            Request("ac") with
            {
                Tag = "release-1",
            },
            CancellationToken.None
        );

        _calls
            .Last()
            .Arguments.Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentbox-rwest-sample/ac:release-1"
            );
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("tag:invalid")]
    [InlineData("")]
    public async Task InvalidRegistryTagsAreRejectedBeforeAccessingBuilds(string tag)
    {
        var action = () =>
            _service.PublishAsync(Request("ac") with { Tag = tag }, CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*not a valid registry tag*"
        );
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheBuiltWorkloadIsPublishedAsAV3SetToJfrog()
    {
        SaveBuild("ac");

        var result = await _service.PublishAsync(Request("ac"), CancellationToken.None);

        result.Should().Be(0);
        var pushes = _calls.Where(call => call.Arguments.Contains("--push")).ToList();
        pushes.Should().HaveCount(3); // workload, portable squad configuration, set
        pushes.Should().OnlyContain(call => call.FileName == "docker");
        _calls
            .Single(call => call.Arguments.Contains("create"))
            .Arguments.Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentbox-rwest-sample/ac:latest"
            );
        pushes.Should().OnlyContain(call => call.Arguments.Contains("--provenance=mode=min"));
        var set = YamlReader.Parse(_descriptors.Last(), "published set");
        set.GetString("kind").Should().Be("set");
        set.GetString("schemaVersion").Should().Be("3");
        set.GetEntries("kits").Should().HaveCount(2);
        _console.AllText.Should().Contain("Published");
    }

    private PublishRequest Request(params string[] squads) =>
        new([new SquadResolver.ResolvedTeam("sample", _root.Path, squads)], "latest");

    [Fact]
    public async Task LegacyBuildsWithoutProvenanceAreRejected()
    {
        SaveBuild("ac");
        var build = _store.Load("ac", "latest")!;
        build.IncludesPersonalConfigurations = null;
        _store.Save(build);

        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*unknown configuration provenance*"
        );
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task LegacyBuildsWithoutFingerprintsMustBeRebuilt()
    {
        SaveBuild("ac");
        var build = _store.Load("ac", "latest")!;
        build.SourceHashes.Clear();
        _store.Save(build);

        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*no source fingerprints*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangedSourcesAreRejectedBeforeAnyPush()
    {
        SaveBuild("ac");
        File.AppendAllText(
            Path.Combine(_store.Load("ac", "latest")!.WorkloadKitDirectory, "workload.yaml"),
            "description: changed\n"
        );

        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*sources changed*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfigurationFilesAndAddedPayloadFilesAreFingerprinted()
    {
        SaveBuild("ac");
        var build = _store.Load("ac", "latest")!;
        build.ConfigFiles.Add(_root.File("squad.abox", "workload: sample\n"));
        SaveVerifiedBuild(build);
        File.AppendAllText(build.ConfigFiles[0], "state: [~/.private]\n");

        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*sources changed*");
        SaveVerifiedBuild(build);
        File.WriteAllText(
            Path.Combine(build.WorkloadKitDirectory, "new-payload.txt"),
            "not part of the build"
        );
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*sources changed*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task PublicationUsesSnapshotsNotSourcesChangedAfterValidation()
    {
        SaveBuild("ac");
        var path = Path.Combine(_store.Load("ac", "latest")!.WorkloadKitDirectory, "workload.yaml");
        var original = File.ReadAllText(path);
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("version")),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                File.WriteAllText(path, original + "description: late change\n");
                return new ProcessResult(0, "", "");
            });

        await _service.PublishAsync(Request("ac"), CancellationToken.None);

        _descriptors[0].Should().Be(original);
        _calls
            .Where(call => call.Arguments.Contains("--push"))
            .Should()
            .OnlyContain(call =>
                !call.Arguments.Last().StartsWith(_root.Path, StringComparison.Ordinal)
            );
    }

    [Fact]
    public async Task SquadReferencesStayStableWhenAnotherSquadIsAdded()
    {
        SaveBuild("ac");
        await _service.PublishAsync(Request("ac"), CancellationToken.None);
        var first = _calls.Single(call => call.Arguments.Contains("create")).Arguments;
        _calls.Clear();
        SaveBuild("bd");

        await _service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        var again = _calls.First(call => call.Arguments.Contains("create")).Arguments;
        first[first.ToList().IndexOf("--tag") + 1]
            .Should()
            .Be(again[again.ToList().IndexOf("--tag") + 1]);
        _calls
            .Where(call => call.Arguments.Contains("--push"))
            .SelectMany(call => call.Arguments)
            .Should()
            .Contain(value => value.Contains("/ac/components/workload:"));
    }

    [Fact]
    public async Task TeamSquadAndComponentSegmentsCannotCollide()
    {
        SaveBuild("ac");
        SaveBuild("workload");
        var second = _store.Load("workload", "latest")!;
        second.Reference = "agentbox-rwest-sample-ac:latest";
        _store.Save(second);
        var request = new PublishRequest(
            [new("sample", _root.Path, ["ac"]), new("sample-ac", _root.Path, ["workload"])],
            "latest"
        );

        await _service.PublishAsync(request, CancellationToken.None);

        _calls
            .Where(call => call.Arguments.Contains("create"))
            .SelectMany(call => call.Arguments)
            .Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentbox-rwest-sample/ac:latest"
            )
            .And.Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentbox-rwest-sample-ac/workload:latest"
            );
    }

    [Fact]
    public async Task CaseCollidingSquadsAreRejectedBeforeLoadingBuilds()
    {
        var action = () => _service.PublishAsync(Request("ac", "AC"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*names collide*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task CaseCollidingTeamsAreRejectedBeforeLoadingBuilds()
    {
        var request = new PublishRequest(
            [new("sample", _root.Path, ["ac"]), new("SAMPLE", _root.Path, ["bd"])],
            "latest"
        );
        var action = () => _service.PublishAsync(request, CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*names collide*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingRemoteAnnotationsStopPublicationBeforePromotion()
    {
        SaveBuild("ac");
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--raw")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, "{}", ""));

        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*did not retain a valid v3 workload descriptor*"
        );
        await _runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("create")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ACorruptSetCandidateNeverMovesTheRequestedTag()
    {
        SaveBuild("ac");
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call =>
                    call.Arguments.Contains("--raw")
                    && !call.Arguments.Last().Contains("/components/")
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, "{}", ""));

        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*did not retain a valid v3 workload descriptor*"
        );
        _calls.Count(call => call.Arguments.Contains("--push")).Should().Be(3);
        await _runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("create")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task PlatformManifestsAreInspectedEvenWhenTheIndexIsUnannotated()
    {
        SaveBuild("ac");
        var platform = "sha256:" + new string('b', 64);
        var attestation = "sha256:" + new string('c', 64);
        var index = JsonSerializer.Serialize(
            new
            {
                schemaVersion = 2,
                mediaType = "application/vnd.oci.image.index.v1+json",
                manifests = new object[]
                {
                    new
                    {
                        digest = platform,
                        platform = new { os = "linux", architecture = "amd64" },
                    },
                    new
                    {
                        digest = attestation,
                        platform = new { os = "unknown", architecture = "unknown" },
                        annotations = new Dictionary<string, string>
                        {
                            ["vnd.docker.reference.type"] = "attestation-manifest",
                        },
                    },
                },
            }
        );
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call =>
                    call.Arguments.Contains("--raw")
                    && call.Arguments.Last().Contains("/components/workload")
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => new ProcessResult(
                0,
                call.Arg<ProcessRequest>().Arguments.Last().EndsWith(platform)
                    ? PublishTestProcess.Manifest("workload")
                    : index,
                ""
            ));

        await _service.PublishAsync(Request("ac"), CancellationToken.None);

        await _runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Last().EndsWith(platform)),
                Arg.Any<CancellationToken>()
            );
        await _runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Last().EndsWith(attestation)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SuppliedLocalBuildPhaseArgumentsAreRejectedBeforeAnyPush()
    {
        SaveBuild("ac");
        var build = _store.Load("ac", "latest")!;
        var local = Path.Combine(_root.Path, "build-arg-kit");
        Directory.CreateDirectory(local);
        File.WriteAllText(
            Path.Combine(local, "spec.yaml"),
            "schemaVersion: \"3\"\nkind: mixin\nargs:\n  version:\n    buildArg: VERSION\n"
        );
        build.Kits.Add(
            new ManifestKit
            {
                IsLocal = true,
                Name = "tool",
                Reference = local,
                Arguments = new() { ["version"] = "1" },
            }
        );
        SaveVerifiedBuild(build);

        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*build-phase arguments is not supported*"
        );
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SuppliedRegistryBuildPhaseArgumentsAreRejectedBeforeAnyPush()
    {
        SaveBuild("ac");
        var build = _store.Load("ac", "latest")!;
        build.Kits.Add(
            new ManifestKit
            {
                Name = "tool",
                Reference = "ghcr.io/example/tool:1",
                Arguments = new() { ["version"] = "1" },
            }
        );
        SaveVerifiedBuild(build);
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--raw")),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new ProcessResult(
                    0,
                    PublishTestProcess.Manifest(
                        "mixin",
                        new { version = new { buildArg = "VERSION" } }
                    ),
                    ""
                )
            );

        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*build-phase arguments is not supported*"
        );
        _calls.Should().OnlyContain(call => !call.Arguments.Contains("--push"));
    }

    [Fact]
    public async Task CleanupFailureDoesNotHideThePublishingError()
    {
        SaveBuild("ac");
        string? blocked = null;
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--push")),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var path = call.Arg<ProcessRequest>().Arguments.Last();
                while (
                    !Path.GetFileName(path).StartsWith("abox-publish-", StringComparison.Ordinal)
                )
                {
                    path = Path.GetDirectoryName(path)!;
                }

                Directory.Delete(path, true);
                File.WriteAllText(path, "blocks directory cleanup");
                blocked = path;
                return new ProcessResult(1, "", "original publication failure");
            });
        try
        {
            var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

            (await action.Should().ThrowAsync<AboxException>()).WithMessage(
                "*original publication failure*"
            );
            _console.AllText.Should().Contain("warning: could not remove publication directory");
        }
        finally
        {
            if (blocked is not null)
                File.Delete(blocked);
        }
    }

    private void SaveBuild(
        string squad,
        string repository = "mtiae-developer-container-images-local"
    )
    {
        var workload = Path.Combine(_store.DirectoryFor(squad, "latest"), "workload");
        Directory.CreateDirectory(workload);
        File.WriteAllText(
            Path.Combine(workload, "workload.yaml"),
            "# syntax=docker/sandbox-kit:3\nschemaVersion: \"3\"\nkind: workload\n"
        );
        SaveVerifiedBuild(
            new BuildManifest
            {
                Squad = squad,
                Tag = "latest",
                Reference = "agentbox-rwest-sample:latest",
                PublishRepository = repository,
                WorkloadKitDirectory = workload,
            }
        );
    }

    [Fact]
    public async Task PublishingUsesTheOriginalVerifiedWorkloadNotTheLocalThinRecipe()
    {
        SaveBuild("ac");
        var build = _store.Load("ac", "latest")!;
        const string recipe = "FROM example.com/base:1\nRUN install-workload-tools\n";
        File.WriteAllText(Path.Combine(build.WorkloadKitDirectory, "workload.dockerfile"), recipe);
        build.LocalWorkload = LocalWorkloadTest.Artifact(_root);
        SaveVerifiedBuild(build);
        string? publishedRecipe = null;
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("--push")
                    && request.Arguments.Any(argument => argument.Contains("/components/workload:"))
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                publishedRecipe = File.ReadAllText(
                    Path.Combine(request.Arguments.Last(), "workload.dockerfile")
                );
                return PublishTestProcess.Run(request);
            });

        await _service.PublishAsync(Request("ac"), CancellationToken.None);

        publishedRecipe.Should().Be(recipe);
        _configurations.Should().NotBeEmpty();
        _configurations
            .Should()
            .OnlyContain(configuration => !configuration.Contains(build.LocalWorkload.Reference));
    }

    private void SaveVerifiedBuild(BuildManifest build)
    {
        build.IncludesPersonalConfigurations = false;
        build.SourceHashes = BuildSources.Capture(build);
        _store.Save(build);
    }
}
