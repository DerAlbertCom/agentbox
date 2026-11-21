using System.Text.Json;
using Abox.Configuration;
using Abox.Features.Build;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sandboxes;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SandboxCreatorTeamTests
{
    private readonly SandboxCreatorFixture _fixture = new();
    private TempDirectory _root => _fixture.Root;
    private TestConsole _console => _fixture.Console;
    private ISbxClient _sbx => _fixture.Sbx;
    private IPublishedSquadSource _published => _fixture.Published;
    private AboxPaths _paths => _fixture.Paths;
    private BuildStore _store => _fixture.Store;
    private SandboxRecordStore _records => _fixture.Records;
    private SandboxCreator _service => _fixture.Service;

    private SandboxCreationRequest Request() => _fixture.Request();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalCreationUsesEachTeamsSharedSquadBuild(bool reverse)
    {
        // Arrange
        var teams = await BuildSharedSquadsAsync(reverse);
        _sbx.ClearReceivedCalls();

        // Act
        foreach (var team in teams)
        {
            var location = _root.CreateDirectory("locations/" + team.Name);
            (
                await _service.CreateAsync(
                    Request() with
                    {
                        Team = team,
                        Location = location,
                    },
                    CancellationToken.None
                )
            )
                .Should()
                .Be(0);
        }

        // Assert
        foreach (var team in teams)
        {
            var build = _store.Load(new SquadBuildIdentity(team.Name, "ac", "latest"))!;
            var location = Path.Combine(_root.Path, "locations", team.Name);
            await _sbx.Received(1)
                .CreateAsync(
                    Arg.Is<SbxCreateRequest>(request =>
                        request.Name == SandboxName.For(location)
                        && request.KitSource == build.LocalWorkload!.RuntimeKitDirectory
                        && request.Mixins.Contains(build.StateMixinDirectory!)
                        && request.Mounts.Single().SandboxPath == "/home/agent/." + team.Name
                        && request.PullPolicy == PullPolicy.Never
                    ),
                    Arg.Any<CancellationToken>()
                );
            var record = _records.Load(SandboxName.For(location))!;
            record.Team.Should().Be(team.Name);
            record.BuildCreatedAt.Should().Be(build.CreatedAt);
            BuildSources.EnsureUnchanged(build);
        }
        _published.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task OwnedLegacyLocalCreationDoesNotMigrateOrRelabelItsBuild()
    {
        // Arrange
        var image = LocalWorkloadFactory.Artifact(_root);
        var original = JsonSerializer.Serialize(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = "agentic-box-rwest-sample:latest",
                LocalWorkload = image,
            }
        );
        var legacy = _root.File("state/builds/ac/latest/build.json", original);

        // Act
        var exitCode = await _service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request => request.KitSource == image.RuntimeKitDirectory),
                Arg.Any<CancellationToken>()
            );
        File.ReadAllText(legacy).Should().Be(original);
        Directory.Exists(Path.Combine(_paths.BuildsDirectory, "sample")).Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("agentic-box-rwest-other:latest")]
    public async Task UnownedLegacyLocalCreationCannotPrepareOrReplaceASandbox(string? reference)
    {
        // Arrange
        _root.File(
            "state/builds/ac/latest/build.json",
            JsonSerializer.Serialize(
                new BuildManifest
                {
                    Squad = "ac",
                    Tag = "latest",
                    Reference = reference,
                    LocalWorkload = LocalWorkloadFactory.Artifact(_root),
                }
            )
        );

        // Act
        var create = () =>
            _service.CreateAsync(Request() with { AssumeYes = true }, CancellationToken.None);

        // Assert
        (await create.Should().ThrowAsync<AboxException>()).WithMessage(
            "*abox build --team sample --tag latest*"
        );
        _sbx.ReceivedCalls().Should().BeEmpty();
        _records.Load(SandboxName.For(_root.Path)).Should().BeNull();
    }

    private async Task<IReadOnlyList<SquadResolver.ResolvedTeam>> BuildSharedSquadsAsync(
        bool reverse
    )
    {
        foreach (var team in new[] { "sample", "other" })
        {
            _root.File($"teams/{team}/ac.abox", $"workload: {team}\nstate: [~/.{team}]\n");
            _root.File($"teams/{team}/{team}.workload", $"template: example.com/{team}:1\n");
        }
        var resolver = new SquadResolver(new AboxConfig());
        var names = reverse ? new[] { "other", "sample" } : new[] { "sample", "other" };
        var teams = names.Select(team => resolver.ResolveTeam(_root.Path, team)).ToList();
        var build = TestServiceGraphs.Build(
            resolver,
            new PersonalConfigurations(_paths),
            _store,
            LocalWorkloadFactory.Images(),
            _sbx,
            _console
        );
        await build.BuildAsync(
            new BuildRequest(
                teams,
                _root.Path,
                "latest",
                Verify: false,
                IncludePersonalConfigurations: false
            ),
            CancellationToken.None
        );
        return teams;
    }
}
