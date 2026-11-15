using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

[Collection("Create command working directory")]
public sealed class CreateDiscoveryTests
{
    [Theory]
    [InlineData("implicit")]
    [InlineData("relative")]
    [InlineData("absolute")]
    public async Task LocalCreateUsesOwningProjectKitsAndExplicitRootsOverrideLocation(string mode)
    {
        // Arrange
        using var project = new SourceProjectFixture("config/teams", "shared/kits");
        var buildExit = await project.InvokeAsync(project.ProjectDirectory, "build");
        var build = project.LoadBuild();
        project.Root.File("workspace/config/teams/sample/wrong.abox", "workload: wrong\n");
        var location = mode == "implicit" ? project.NestedDirectory : project.Workspace;
        string[] rootOptions = mode switch
        {
            "relative" => ["--root", "project/src/app/deep"],
            "absolute" => ["--root", project.NestedDirectory],
            _ => [],
        };

        // Act
        var exitCode = await project.InvokeAsync(
            project.Root.Path,
            ["create", "--local", "--squad", "ac", "--location", location, .. rootOptions]
        );

        // Assert
        buildExit.Should().Be(0, project.Console.AllText);
        exitCode.Should().Be(0, project.Console.AllText);
        await project
            .Sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.KitSource == build.LocalWorkload!.RuntimeKitDirectory
                    && request.Location == location
                    && request
                        .Mixins.Select(Path.GetFullPath)
                        .SequenceEqual(new[] { project.ProjectKit, project.PersonalKit })
                    && request.PullPolicy == "never"
                ),
                Arg.Any<CancellationToken>()
            );
        project.Console.AllText.Should().NotContain("There is no squad");
    }
}
