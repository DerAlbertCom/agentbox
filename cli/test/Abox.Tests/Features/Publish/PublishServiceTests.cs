using System.Text.Json;
using Abox.Configuration;
using Abox.Features.Publish;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using NSubstitute;

namespace Abox.Tests;

public sealed class PublishServiceTests
{
    private readonly PublishServiceFixture _fixture = new();
    private TempDirectory _root => _fixture.Root;
    private BuildStore _store => _fixture.Store;
    private IProcessRunner _runner => _fixture.Runner;
    private IHostPrograms _programs => _fixture.Programs;
    private TestConsole _console => _fixture.Console;
    private List<ProcessRequest> _calls => _fixture.Calls;
    private List<string> _descriptors => _fixture.Descriptors;
    private List<string> _configurations => _fixture.Configurations;
    private PublishService _service => _fixture.Service;

    private void SaveRegistryKitBuild() => _fixture.SaveRegistryKitBuild();

    [Fact]
    public async Task PersonalConfigurationKitsCannotBePublished()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\n");
        _root.File("teams/sample/sample.workload", "template: example.com/base:1\n");
        _root.File("kits/private/private.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        var configDirectory = Path.Combine(_root.Path, "config");
        _root.File("config/personal.abox", "kits:\n  - private\n");
        var build = TestServiceGraphs.Build(
            new SquadResolver(new AboxConfig()),
            new PersonalConfigurations(AboxPaths.For(configDirectory, _root.Path)),
            _store,
            LocalWorkloadFactory.Images(),
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

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*personal configuration*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task LocalAndRegistryKitsKeepTheirArgumentsAndStateIsIncluded()
    {
        // Arrange
        SaveBuild("ac");
        var build = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
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

        // Act
        await _service.PublishAsync(
            Request("ac") with
            {
                Builder = "custom-builder",
            },
            CancellationToken.None
        );

        // Assert
        var pushes = _calls.Where(call => call.Arguments.Contains("--push")).ToList();
        pushes.Should().HaveCount(5);
        pushes.Should().OnlyContain(call => call.Arguments.Contains("custom-builder"));
        pushes.Should().OnlyContain(call => !call.Arguments.Contains("--build-arg"));
        var set = YamlReader.Parse(_descriptors.Last(), "set");
        var kits = set.GetEntries("kits");
        kits.Select(kit => kit.GetString("ref")!.Split('@')[0])
            .Should()
            .Equal(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac/components/workload",
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac/components/kit-0",
                "ghcr.io/example/tool:1",
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac/components/state",
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac/components/config"
            );
        kits[1].GetEntry("args")!.GetString("url").Should().Be(build.Kits[0].Arguments["url"]);
        kits[2]
            .GetEntry("args")!
            .GetString("message")
            .Should()
            .Be(build.Kits[1].Arguments["message"]);
        kits[3].GetEntry("args").Should().BeNull();
        using var config = JsonDocument.Parse(_configurations.Single());
        config.RootElement.GetProperty("SchemaVersion").GetInt32().Should().Be(1);
        AssertBundledRuntimeMetadata(config.RootElement);
        _configurations.Single().Should().NotContain(_root.Path);
    }

    private static void AssertBundledRuntimeMetadata(JsonElement metadata)
    {
        AssertPublicJson(
            metadata.GetProperty("Mcp"),
            """
            [{"Name": "elastic", "Url": "https://example.com/mcp"}]
            """
        );
        AssertPublicJson(
            metadata.GetProperty("State"),
            """
            [{"SandboxPath": "/home/agent/.state", "RelativePath": ".state"}]
            """
        );
        AssertPublicJson(
            metadata.GetProperty("Kits"),
            """
            [
              {"Name": "local", "Arguments": {"url": "https://example.com/a?x=hello world&y=1"}},
              {"Name": "remote", "Arguments": {"message": "quoted: \"hello\"\nnext line"}}
            ]
            """
        );
    }

    private static void AssertPublicJson(JsonElement actual, string expectedJson)
    {
        using var expected = JsonDocument.Parse(expectedJson);
        JsonElement
            .DeepEquals(actual, expected.RootElement)
            .Should()
            .BeTrue(
                "expected public metadata {0}, but found {1}",
                expectedJson,
                actual.GetRawText()
            );
    }

    [Fact]
    public async Task EachSquadPublishesToItsOwnWorkloadRepository()
    {
        // Arrange
        SaveBuild("ac", "first-images-local");
        SaveBuild("bd", "second-images-local");

        // Act
        await _service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        // Assert
        var tags = _calls
            .Where(call => call.Arguments.Contains("--tag"))
            .Select(call => call.Arguments[call.Arguments.ToList().IndexOf("--tag") + 1])
            .ToList();
        tags.Should().HaveCount(8);
        tags.Take(4)
            .Should()
            .OnlyContain(tag =>
                tag.StartsWith("rwest.jfrog.io/first-images-local/agentic-box-rwest-sample/ac")
            );
        tags.Skip(4)
            .Should()
            .OnlyContain(tag =>
                tag.StartsWith("rwest.jfrog.io/second-images-local/agentic-box-rwest-sample/bd")
            );
        tags.Should()
            .Contain("rwest.jfrog.io/first-images-local/agentic-box-rwest-sample/ac:latest");
        tags.Should()
            .Contain("rwest.jfrog.io/second-images-local/agentic-box-rwest-sample/bd:latest");
        foreach (var descriptor in _descriptors.Where(text => text.Contains("\"kind\": \"set\"")))
        {
            descriptor.Should().NotContain("mtiae-developer-container-images-local");
        }
    }

    [Fact]
    public async Task MultipleSquadsDoNotOverwriteEachOther()
    {
        // Arrange
        SaveBuild("ac");
        SaveBuild("bd");

        // Act
        await _service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        // Assert
        _calls
            .SelectMany(call => call.Arguments)
            .Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac:latest"
            );
        _calls
            .SelectMany(call => call.Arguments)
            .Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/bd:latest"
            );
        _calls
            .SelectMany(call => call.Arguments)
            .Should()
            .NotContain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample:latest"
            );
    }

    [Fact]
    public async Task LegacyBuildsWithoutAPublishRepositoryMustBeRebuilt()
    {
        // Arrange
        SaveBuild("ac");
        var build = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.PublishRepository = null;
        _store.Save(build);

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
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
        // Arrange
        SaveBuild("ac");
        SaveBuild("bd", repository);

        // Act
        var action = () => _service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*publishRepository*");
        _calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("elastic,other", null)]
    [InlineData("elastic", "file:///local")]
    [InlineData("elastic", "https://user:password@example.com/mcp")]
    public async Task EveryBuildIsCheckedBeforeAnythingIsPublished(string? name, string? url)
    {
        // Arrange
        SaveBuild("ac");
        if (name is not null)
        {
            SaveBuild("bd");
            var build = _store.Load(new SquadBuildIdentity("sample", "bd", "latest"))!;
            build.Mcp.Add(new ManifestMcp { Name = name, Url = url });
            _store.Save(build);
        }

        // Act
        var action = () => _service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            name is null ? "*abox build --team sample --tag latest*" : "*MCP server*mcp.*not valid*"
        );
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ASquadBuildFromAnotherTeamIsNotPublished()
    {
        // Arrange
        SaveBuild("ac");
        var build = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.Reference = "agentic-box-rwest-other:latest";
        File.WriteAllText(
            ManifestPath(new("sample", "ac", "latest")),
            JsonSerializer.Serialize(build)
        );

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*no proven ownership for team 'sample'*abox build --team sample*"
        );
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AMissingBuiltKitDoesNotStartAPublish()
    {
        // Arrange
        SaveBuild("ac");
        File.Delete(
            Path.Combine(
                _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!.WorkloadKitDirectory,
                "workload.yaml"
            )
        );

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*has no descriptor*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AMissingDockerGetsAnInstallHint()
    {
        // Arrange
        SaveBuild("ac");
        _programs.Exists("docker").Returns(false);

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*Docker with the Buildx plugin*"
        );
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task AFailedPushDoesNotMoveTheSetTagAndGetsAnAuthenticationHint()
    {
        // Arrange
        SaveBuild("ac");
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--push")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(1, string.Empty, "unauthorized"));

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
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
        // Arrange
        SaveBuild("ac");
        await _service.PublishAsync(Request("ac"), CancellationToken.None);
        var firstSet = YamlReader.Parse(_descriptors.Last(), "first set");

        // Act
        await _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
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
        // Arrange
        SaveBuild("ac");
        var build = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.Tag = "release-1";
        build.Reference = "agentic-box-rwest-sample:release-1";
        _store.Save(build);

        // Act
        await _service.PublishAsync(
            Request("ac") with
            {
                Tag = "release-1",
            },
            CancellationToken.None
        );

        // Assert
        _calls
            .Last()
            .Arguments.Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac:release-1"
            );
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("tag:invalid")]
    [InlineData("")]
    public async Task InvalidRegistryTagsAreRejectedBeforeAccessingBuilds(string tag)
    {
        // Arrange: the fixture has no builds for the invalid tag.
        // Act
        var action = () =>
            _service.PublishAsync(Request("ac") with { Tag = tag }, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*not a valid registry tag*"
        );
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task TheBuiltWorkloadIsPublishedAsAV3SetToJfrog()
    {
        // Arrange
        SaveBuild("ac");

        // Act
        var result = await _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        result.Should().Be(0);
        var pushes = _calls.Where(call => call.Arguments.Contains("--push")).ToList();
        pushes.Should().HaveCount(3); // workload, portable squad configuration, set
        pushes.Should().OnlyContain(call => call.FileName == "docker");
        _calls
            .Single(call => call.Arguments.Contains("create"))
            .Arguments.Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac:latest"
            );
        pushes.Should().OnlyContain(call => call.Arguments.Contains("--provenance=mode=min"));
        var set = YamlReader.Parse(_descriptors.Last(), "published set");
        set.GetString("kind").Should().Be("set");
        set.GetString("schemaVersion").Should().Be("3");
        set.GetEntries("kits").Should().HaveCount(2);
        _console.AllText.Should().Contain("Published");
    }

    private PublishRequest Request(params string[] squads) => _fixture.Request(squads);

    [Fact]
    public async Task PublishedSetOmitsOnlyAbsentComponentArguments()
    {
        // Arrange
        SaveRegistryKitBuild();

        // Act
        await _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        using var set = JsonDocument.Parse(_descriptors.Last().Split('\n', 2)[1]);
        var descriptor = set.RootElement;
        descriptor
            .EnumerateObject()
            .Select(field => field.Name)
            .Should()
            .Equal("schemaVersion", "kind", "displayName", "kits");
        descriptor.GetProperty("schemaVersion").GetString().Should().Be("3");
        descriptor.GetProperty("kind").GetString().Should().Be("set");
        descriptor.GetProperty("displayName").GetString().Should().Be("sample / ac");
        var components = descriptor.GetProperty("kits").EnumerateArray().ToArray();
        components[0].EnumerateObject().Select(field => field.Name).Should().Equal("ref");
        components[1].EnumerateObject().Select(field => field.Name).Should().Equal("ref", "args");
        components[1].GetProperty("args").EnumerateObject().Should().BeEmpty();
        components[2].EnumerateObject().Select(field => field.Name).Should().Equal("ref");
    }

    [Fact]
    public async Task PublishedConfigurationIsAV3MixinWithOnlyThePublicMetadataPayload()
    {
        // Arrange
        SaveBuild("ac");

        // Act
        await _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        // The syntax header is literal LF; indented JSON uses the platform newline.
        _descriptors[^2]
            .Should()
            .Be(
                "# syntax=docker/sandbox-kit:3\n"
                    + """
                    {
                      "schemaVersion": "3",
                      "kind": "mixin",
                      "build": "FROM scratch\nCOPY squad.json /usr/share/abox/squad.json\n"
                    }
                    """.ReplaceLineEndings(Environment.NewLine)
            );
    }

    [Fact]
    public async Task PublishedSquadMetadataExcludesLocalProvenanceAndKeepsNullAndEmptyPublicFields()
    {
        // Arrange
        SaveRegistryKitBuild();
        var build = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.CreatedAt = DateTimeOffset.Parse("2026-01-02T03:04:05+00:00");
        build.Mcp.Add(new ManifestMcp { Name = "host-server" });
        build.ConfigFiles.Add(_root.File("private.abox", "secret: synthetic-private-value\n"));
        SaveVerifiedBuild(build);

        // Act
        await _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        using var actual = JsonDocument.Parse(_configurations.Single());
        AssertPublicJson(
            actual.RootElement,
            """
            {
              "SchemaVersion": 1,
              "Team": "sample",
              "Squad": "ac",
              "Tag": "latest",
              "CreatedAt": "2026-01-02T03:04:05+00:00",
              "Mcp": [{"Name": "host-server", "Url": null}],
              "State": [],
              "Kits": [{"Name": "tool", "Arguments": {}}]
            }
            """
        );
        _configurations
            .Single()
            .Should()
            .NotContain(_root.Path)
            .And.NotContain("synthetic-private-value");
    }

    [Fact]
    public async Task LegacyBuildsWithoutProvenanceAreRejected()
    {
        // Arrange
        SaveBuild("ac");
        var build = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.IncludesPersonalConfigurations = null;
        _store.Save(build);

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*unknown configuration provenance*"
        );
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task LegacyBuildsWithoutFingerprintsMustBeRebuilt()
    {
        // Arrange
        SaveBuild("ac");
        var build = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.SourceHashes.Clear();
        _store.Save(build);

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*no source fingerprints*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ChangedSourcesAreRejectedBeforeAnyPush()
    {
        // Arrange
        SaveBuild("ac");
        File.AppendAllText(
            Path.Combine(
                _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!.WorkloadKitDirectory,
                "workload.yaml"
            ),
            "description: changed\n"
        );

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*sources changed*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task ConfigurationFilesAndAddedPayloadFilesAreFingerprinted()
    {
        // Arrange
        SaveBuild("ac");
        var build = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        build.ConfigFiles.Add(_root.File("squad.abox", "workload: sample\n"));
        SaveVerifiedBuild(build);
        File.AppendAllText(build.ConfigFiles[0], "state: [~/.private]\n");

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*sources changed*");

        // Arrange
        SaveVerifiedBuild(build);
        File.WriteAllText(
            Path.Combine(build.WorkloadKitDirectory, "new-payload.txt"),
            "not part of the build"
        );

        // Act / Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*sources changed*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task PublicationUsesSnapshotsNotSourcesChangedAfterValidation()
    {
        // Arrange
        SaveBuild("ac");
        var path = Path.Combine(
            _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!.WorkloadKitDirectory,
            "workload.yaml"
        );
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

        // Act
        await _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
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
        // Arrange
        SaveBuild("ac");
        await _service.PublishAsync(Request("ac"), CancellationToken.None);
        var first = _calls.Single(call => call.Arguments.Contains("create")).Arguments;
        _calls.Clear();
        SaveBuild("bd");

        // Act
        await _service.PublishAsync(Request("ac", "bd"), CancellationToken.None);

        // Assert
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
        // Arrange
        SaveBuild("ac");
        SaveBuild("workload");
        var second = _store.Load(new SquadBuildIdentity("sample", "workload", "latest"))!;
        second.Reference = "agentic-box-rwest-sample-ac:latest";
        _store.Save(second);
        var request = new PublishRequest(
            [new("sample", _root.Path, ["ac"]), new("sample-ac", _root.Path, ["workload"])],
            "latest"
        );

        // Act
        await _service.PublishAsync(request, CancellationToken.None);

        // Assert
        _calls
            .Where(call => call.Arguments.Contains("create"))
            .SelectMany(call => call.Arguments)
            .Should()
            .Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample/ac:latest"
            )
            .And.Contain(
                "rwest.jfrog.io/mtiae-developer-container-images-local/agentic-box-rwest-sample-ac/workload:latest"
            );
    }

    [Fact]
    public async Task CaseCollidingSquadsAreRejectedBeforeLoadingBuilds()
    {
        // Arrange: use the fixture with case-colliding squad selections.
        // Act
        var action = () => _service.PublishAsync(Request("ac", "AC"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*names collide*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task CaseCollidingTeamsAreRejectedBeforeLoadingBuilds()
    {
        // Arrange
        var request = new PublishRequest(
            [new("sample", _root.Path, ["ac"]), new("SAMPLE", _root.Path, ["bd"])],
            "latest"
        );
        // Act
        var action = () => _service.PublishAsync(request, CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*names collide*");
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task MissingRemoteAnnotationsStopPublicationBeforePromotion()
    {
        // Arrange
        SaveBuild("ac");
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(call => call.Arguments.Contains("--raw")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(0, "{}", ""));

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
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
        // Arrange
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

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
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
        // Arrange
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

        // Act
        await _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
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
        // Arrange
        SaveBuild("ac");
        var build = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
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

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*build-phase arguments is not supported*"
        );
        _calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SuppliedRegistryBuildPhaseArgumentsAreRejectedBeforeAnyPush()
    {
        // Arrange
        SaveBuild("ac");
        var build = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
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

        // Act
        var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*build-phase arguments is not supported*"
        );
        _calls.Should().OnlyContain(call => !call.Arguments.Contains("--push"));
    }

    [Fact]
    public async Task CleanupFailureDoesNotHideThePublishingError()
    {
        // Arrange
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
            // Act
            var action = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

            // Assert
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

    private string ManifestPath(SquadBuildIdentity identity) => _fixture.ManifestPath(identity);

    private void SaveBuild(
        string squad,
        string repository = "mtiae-developer-container-images-local"
    ) => _fixture.SaveBuild(squad, repository);

    [Fact]
    public async Task PublishingUsesTheOriginalVerifiedWorkloadNotTheLocalThinRecipe()
    {
        // Arrange
        SaveBuild("ac");
        var build = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        const string recipe = "FROM example.com/base:1\nRUN install-workload-tools\n";
        File.WriteAllText(Path.Combine(build.WorkloadKitDirectory, "workload.dockerfile"), recipe);
        build.LocalWorkload = LocalWorkloadFactory.Artifact(_root);
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

        // Act
        await _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        publishedRecipe.Should().Be(recipe);
        _configurations.Should().NotBeEmpty();
        _configurations
            .Should()
            .OnlyContain(configuration => !configuration.Contains(build.LocalWorkload.Reference));
    }

    private void SaveVerifiedBuild(BuildManifest build) => _fixture.SaveVerifiedBuild(build);
}
