using Abox.Features.Build;
using Abox.LocalState;
using NSubstitute;

namespace Abox.Tests;

public sealed partial class BuildServiceTests
{
    [Theory]
    [InlineData("sample", "other", false)]
    [InlineData("other", "sample", false)]
    [InlineData("sample", "other", true)]
    [InlineData("other", "sample", true)]
    public async Task SharedSquadBuildsKeepEachTeamsGeneratedSourcesAndImage(
        string first,
        string second,
        bool metadataOnly
    )
    {
        // Arrange
        _root.File("teams/other/ac.abox", "workload: other\n");
        _root.File("teams/other/other.workload", "template: example.com/other:2\n");
        BuildRequest TeamRequest(string team) =>
            new(
                [_resolver.ResolveTeam(_root.Path, team)],
                _root.Path,
                "latest",
                Verify: false,
                IncludePersonalConfigurations: false,
                MetadataOnly: metadataOnly
            );

        // Act
        await _service.BuildAsync(TeamRequest(first), CancellationToken.None);
        var firstBuild = _store.Load(first, "ac", "latest")!;
        var recipe = Path.Combine(firstBuild.WorkloadKitDirectory, "workload.dockerfile");
        var originalRecipe = File.ReadAllText(recipe);
        await _service.BuildAsync(TeamRequest(second), CancellationToken.None);
        var secondBuild = _store.Load(second, "ac", "latest")!;

        // Assert
        File.ReadAllText(recipe).Should().Be(originalRecipe);
        secondBuild.WorkloadKitDirectory.Should().NotBe(firstBuild.WorkloadKitDirectory);
        _store.Load(first, "ac", "latest")!.Reference.Should().Be($"agentbox-rwest-{first}:latest");
        _store
            .Load(second, "ac", "latest")!
            .Reference.Should()
            .Be($"agentbox-rwest-{second}:latest");
        BuildSources.EnsureUnchanged(firstBuild);
        BuildSources.EnsureUnchanged(secondBuild);
        if (metadataOnly)
        {
            firstBuild.LocalWorkload.Should().BeNull();
            secondBuild.LocalWorkload.Should().BeNull();
        }
        else
        {
            firstBuild.LocalWorkload.Should().NotBeNull();
            secondBuild.LocalWorkload.Should().NotBeNull();
            secondBuild
                .LocalWorkload!.Reference.Should()
                .NotBe(firstBuild.LocalWorkload!.Reference);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task MultiTeamBuildsKeepSharedSquadsIndependent(bool reverse, bool metadataOnly)
    {
        // Arrange
        _root.File("teams/other/ac.abox", "workload: other\nstate: [~/.other]\n");
        _root.File("teams/other/other.workload", "template: example.com/other:2\n");
        _root.File("teams/sample/ac.abox", "workload: sample\nstate: [~/.sample]\n");
        var teams = new[] { "sample", "other" };
        if (reverse)
            Array.Reverse(teams);

        // Act
        var exitCode = await _service.BuildAsync(
            new BuildRequest(
                teams.Select(team => _resolver.ResolveTeam(_root.Path, team)).ToList(),
                _root.Path,
                "latest",
                Verify: false,
                IncludePersonalConfigurations: false,
                MetadataOnly: metadataOnly
            ),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        var sample = _store.Load("sample", "ac", "latest")!;
        var other = _store.Load("other", "ac", "latest")!;
        File.ReadAllText(Path.Combine(sample.WorkloadKitDirectory, "workload.dockerfile"))
            .Should()
            .Contain("example.com/base:1")
            .And.NotContain("example.com/other:2");
        File.ReadAllText(Path.Combine(other.WorkloadKitDirectory, "workload.dockerfile"))
            .Should()
            .Contain("example.com/other:2")
            .And.NotContain("example.com/base:1");
        File.ReadAllText(Path.Combine(sample.StateMixinDirectory!, "state.yaml"))
            .Should()
            .Contain(".sample")
            .And.NotContain(".other");
        File.ReadAllText(Path.Combine(other.StateMixinDirectory!, "state.yaml"))
            .Should()
            .Contain(".other")
            .And.NotContain(".sample");
        BuildSources.EnsureUnchanged(sample);
        BuildSources.EnsureUnchanged(other);
        if (!metadataOnly)
        {
            sample.LocalWorkload!.Reference.Should().NotBe(other.LocalWorkload!.Reference);
            sample
                .LocalWorkload.RuntimeKitDirectory.Should()
                .NotBe(other.LocalWorkload.RuntimeKitDirectory);
        }
    }

    [Fact]
    public async Task MetadataOnlyInvalidatesOnlyTheSelectedTeamsImageAndLeavesLegacyArtifactsUntouched()
    {
        // Arrange
        _root.File("teams/other/ac.abox", "workload: other\n");
        _root.File("teams/other/other.workload", "template: example.com/other:2\n");
        var legacy = _root.File("state/builds/ac/latest/build.json", "legacy sentinel");
        var request = new BuildRequest(
            [
                _resolver.ResolveTeam(_root.Path, "sample"),
                _resolver.ResolveTeam(_root.Path, "other"),
            ],
            _root.Path,
            "latest",
            Verify: false,
            IncludePersonalConfigurations: false
        );
        await _service.BuildAsync(request, CancellationToken.None);
        var previous = _store.Load("sample", "ac", "latest")!.LocalWorkload!;
        var other = _store.Load("other", "ac", "latest")!;
        _runner.ClearReceivedCalls();
        _sbx.ClearReceivedCalls();

        // Act
        await _service.BuildAsync(
            request with
            {
                Teams = [request.Teams[0]],
                MetadataOnly = true,
            },
            CancellationToken.None
        );

        // Assert
        _store.Load("sample", "ac", "latest")!.LocalWorkload.Should().BeNull();
        _store
            .Load("other", "ac", "latest")!
            .LocalWorkload!.Reference.Should()
            .Be(other.LocalWorkload!.Reference);
        File.Exists(Path.Combine(previous.RuntimeKitDirectory, "workload.yaml")).Should().BeTrue();
        File.ReadAllText(legacy).Should().Be("legacy sentinel");
        BuildSources.EnsureUnchanged(other);
        _runner.ReceivedCalls().Should().BeEmpty();
        _sbx.ReceivedCalls().Should().BeEmpty();
    }
}
