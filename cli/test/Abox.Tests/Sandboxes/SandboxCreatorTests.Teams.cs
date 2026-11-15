using System.Text.Json;
using Abox.Configuration;
using Abox.Features.Build;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed partial class SandboxCreatorTests
{
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
            var location = _root.Directory_("locations/" + team.Name);
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
            var build = _store.Load(team.Name, "ac", "latest")!;
            var location = Path.Combine(_root.Path, "locations", team.Name);
            await _sbx.Received(1)
                .CreateAsync(
                    Arg.Is<SbxCreateRequest>(request =>
                        request.Name == SandboxName.For(location)
                        && request.KitSource == build.LocalWorkload!.RuntimeKitDirectory
                        && request.Mixins.Contains(build.StateMixinDirectory!)
                        && request.Mounts.Single().SandboxPath == "/home/agent/." + team.Name
                        && request.PullPolicy == "never"
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
        var image = LocalWorkloadTest.Artifact(_root);
        var original = JsonSerializer.Serialize(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = "agentbox-rwest-sample:latest",
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
    [InlineData("agentbox-rwest-other:latest")]
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
                    LocalWorkload = LocalWorkloadTest.Artifact(_root),
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
