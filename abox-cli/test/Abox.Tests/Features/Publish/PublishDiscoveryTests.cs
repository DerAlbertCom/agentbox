using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

[Collection("Create command working directory")]
public sealed class PublishDiscoveryTests
{
    [Theory]
    [InlineData("implicit")]
    [InlineData("relative")]
    [InlineData("absolute")]
    public async Task PublicationUsesTheBuiltOwningProjectWithNestedOrExplicitRoots(string mode)
    {
        // Arrange
        using var project = new SourceProjectFixture("config/teams", "shared/kits");
        var workingDirectory = mode == "implicit" ? project.NestedDirectory : project.Root.Path;
        string[] rootOptions = mode switch
        {
            "relative" => ["--root", "project/src/app/deep"],
            "absolute" => ["--root", project.NestedDirectory],
            _ => [],
        };
        var buildExit = await project.InvokeAsync(
            workingDirectory,
            ["build", "--metadata-only", "--no-personal", .. rootOptions]
        );
        project.Runner.ClearReceivedCalls();

        // Act
        var exitCode = await project.InvokeAsync(workingDirectory, ["publish", .. rootOptions]);

        // Assert
        buildExit.Should().Be(0, project.Console.AllText);
        exitCode.Should().Be(0, project.Console.AllText);
        var manifest = project.LoadBuild();
        manifest.RootDirectory.Should().Be(project.ProjectDirectory);
        Path.GetFullPath(manifest.Kits.Should().ContainSingle().Which.Reference)
            .Should()
            .Be(project.ProjectKit);
        manifest.IncludesPersonalConfigurations.Should().BeFalse();
        project
            .Console.AllText.Should()
            .Contain("rwest.jfrog.io/source-images-local/agentic-box-rwest-sample/ac:release");
        await project
            .Runner.Received(4)
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("--push")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task NestedPublicationRejectsChangesToTheOwningProjectKitBeforeDocker()
    {
        // Arrange
        using var project = new SourceProjectFixture();
        var buildExit = await project.InvokeAsync(
            project.ProjectDirectory,
            "build",
            "--metadata-only",
            "--no-personal"
        );
        File.AppendAllText(Path.Combine(project.ProjectKit, "project-kit.yaml"), "\n# edited\n");
        project.Runner.ClearReceivedCalls();

        // Act
        var exitCode = await project.InvokeAsync(project.NestedDirectory, "publish");

        // Assert
        buildExit.Should().Be(0);
        exitCode.Should().Be(1);
        project.Console.AllText.Should().Contain("sources changed");
        project.Runner.ReceivedCalls().Should().BeEmpty();
    }
}
