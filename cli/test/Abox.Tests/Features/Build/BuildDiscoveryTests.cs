using NSubstitute;

namespace Abox.Tests;

[Collection("Create command working directory")]
public sealed class BuildDiscoveryTests
{
    [Theory]
    [InlineData("teams", "kits", false)]
    [InlineData("config/teams", "shared/kits", true)]
    public async Task NestedBuildUsesTheSameProjectKitsWorkloadAndPersonalAdditionsAsTheRoot(
        string teams,
        string kits,
        bool metadataOnly
    )
    {
        // Arrange
        using var project = new SourceProjectFixture(teams, kits);
        var arguments = metadataOnly ? new[] { "build", "--metadata-only" } : ["build"];

        // Act
        var rootExit = await project.InvokeAsync(project.ProjectDirectory, arguments);
        var fromRoot = project.LoadBuild();
        var nestedExit = await project.InvokeAsync(project.NestedDirectory, arguments);
        var fromNested = project.LoadBuild();

        // Assert
        rootExit.Should().Be(0, project.Console.AllText);
        nestedExit.Should().Be(0, project.Console.AllText);
        fromNested.RootDirectory.Should().Be(project.ProjectDirectory);
        fromNested.Kits.Should().BeEquivalentTo(fromRoot.Kits);
        fromNested.ConfigFiles.Should().Equal(fromRoot.ConfigFiles);
        fromNested
            .Kits.Select(kit => Path.GetFullPath(kit.Reference))
            .Should()
            .Equal(project.ProjectKit, project.PersonalKit);
        fromNested.IncludesPersonalConfigurations.Should().BeTrue();
        File.ReadAllText(Path.Combine(fromNested.WorkloadKitDirectory, "scripts", "setup.sh"))
            .Should()
            .Contain("owning-project-workload");
        if (metadataOnly)
        {
            project.Sbx.ReceivedCalls().Should().BeEmpty();
            project.Runner.ReceivedCalls().Should().BeEmpty();
        }
        else
        {
            await project
                .Sbx.Received(2)
                .InspectKitAsync(
                    project.ProjectKit,
                    Arg.Any<CancellationToken>(),
                    Arg.Any<IReadOnlyDictionary<string, string>?>()
                );
        }
    }

    [Fact]
    public async Task RepeatedExplicitTeamsReplaceTheDefaultRatherThanAddingIt()
    {
        // Arrange
        using var project = new SourceProjectFixture();
        project.Root.File(
            "project/teams/one/first.abox",
            "workload: shared\nkits: [project-kit]\n"
        );
        project.Root.File(
            "project/teams/two/second.abox",
            "workload: shared\nkits: [project-kit]\n"
        );

        // Act
        var exitCode = await project.InvokeAsync(
            project.NestedDirectory,
            "build",
            "--team",
            "one",
            "--team",
            "two",
            "--metadata-only",
            "--no-personal"
        );

        // Assert
        exitCode.Should().Be(0, project.Console.AllText);
        project.LoadBuild("first").Reference.Should().Be("agentbox-rwest-one:release");
        project.LoadBuild("second").Reference.Should().Be("agentbox-rwest-two:release");
        project.LoadBuild().Should().BeNull("explicit selections replace defaultTeam 'sample'");
        project.Runner.ReceivedCalls().Should().BeEmpty();
        project.Sbx.ReceivedCalls().Should().BeEmpty();
    }
}
