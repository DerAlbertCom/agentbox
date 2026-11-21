using System.Text.Json;
using System.Text.Json.Nodes;
using Abox.Configuration;
using Abox.Features.Build;
using Abox.Features.Publish;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class PublishServiceTeamTests
{
    private readonly PublishServiceFixture _fixture = new();
    private TempDirectory _root => _fixture.Root;
    private BuildStore _store => _fixture.Store;
    private TestConsole _console => _fixture.Console;
    private List<ProcessRequest> _calls => _fixture.Calls;
    private List<string> _descriptors => _fixture.Descriptors;
    private List<string> _configurations => _fixture.Configurations;
    private PublishService _service => _fixture.Service;

    private PublishRequest Request(params string[] squads) => _fixture.Request(squads);

    private void SaveBuild(string squad) => _fixture.SaveBuild(squad);

    private string ManifestPath(SquadBuildIdentity identity) => _fixture.ManifestPath(identity);

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task PublicationSelectsEachTeamsSharedSquadWithoutChangingPublishedNames(
        bool reverse,
        bool metadataOnly
    )
    {
        // Arrange
        var resolver = new SquadResolver(new AboxConfig());
        foreach (var team in new[] { "sample", "other" })
        {
            _root.File($"teams/{team}/ac.abox", $"workload: {team}\nstate: [~/.{team}]\n");
            _root.File(
                $"teams/{team}/{team}.workload",
                $"template: example.com/{team}:1\npublishRepository: {team}-images-local\n"
            );
        }
        var names = reverse ? new[] { "other", "sample" } : new[] { "sample", "other" };
        var teams = names.Select(team => resolver.ResolveTeam(_root.Path, team)).ToList();
        var build = TestServiceGraphs.Build(
            resolver,
            new PersonalConfigurations(AboxPaths.For(_root.Path, _root.Path)),
            _store,
            LocalWorkloadFactory.Images(),
            Substitute.For<ISbxClient>(),
            _console
        );
        foreach (var team in teams)
        {
            await build.BuildAsync(
                new BuildRequest(
                    [team],
                    _root.Path,
                    "latest",
                    Verify: false,
                    IncludePersonalConfigurations: false,
                    MetadataOnly: metadataOnly
                ),
                CancellationToken.None
            );
        }

        // Act
        var exitCode = await _service.PublishAsync(
            new PublishRequest(teams, "latest"),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        _configurations.Should().HaveCount(2);
        foreach (var team in names)
        {
            _descriptors
                .Should()
                .Contain(descriptor => descriptor.Contains($"displayName: \"{team}\"\n"));
            var configuration = _configurations.Single(text =>
                JsonNode.Parse(text)!["Team"]!.GetValue<string>() == team
            );
            using var json = JsonDocument.Parse(configuration);
            json.RootElement.GetProperty("Squad").GetString().Should().Be("ac");
            json.RootElement.GetProperty("State")[0]
                .GetProperty("SandboxPath")
                .GetString()
                .Should()
                .Be("/home/agent/." + team);
            _calls
                .Where(call => call.Arguments.Contains("create"))
                .Should()
                .Contain(call =>
                    call.Arguments.Contains(
                        $"rwest.jfrog.io/{team}-images-local/agentic-box-rwest-{team}/ac:latest"
                    )
                );
        }
    }

    [Theory]
    [InlineData("sample", true)]
    [InlineData("other", false)]
    [InlineData(null, false)]
    public async Task LegacyPublicationRequiresExplicitMatchingOwnershipWithoutMigration(
        string? owner,
        bool allowed
    )
    {
        // Arrange
        SaveBuild("ac");
        var manifest = _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        manifest.Reference = owner is null ? null : $"agentic-box-rwest-{owner}:latest";
        var original = JsonSerializer.Serialize(manifest);
        var legacy = _root.File("state/builds/ac/latest/build.json", original);
        var qualified = ManifestPath(new("sample", "ac", "latest"));
        File.Delete(qualified);

        // Act
        var publish = () => _service.PublishAsync(Request("ac"), CancellationToken.None);

        // Assert
        if (allowed)
        {
            (await publish()).Should().Be(0);
            _configurations.Should().ContainSingle();
        }
        else
        {
            (await publish.Should().ThrowAsync<AboxException>()).WithMessage(
                "*abox build --team sample --tag latest*"
            );
            _calls.Should().BeEmpty();
        }
        File.ReadAllText(legacy).Should().Be(original);
        File.Exists(qualified).Should().BeFalse();
    }
}
