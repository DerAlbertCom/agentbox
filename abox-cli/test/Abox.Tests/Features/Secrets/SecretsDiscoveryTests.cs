using NSubstitute;

namespace Abox.Tests;

[Collection("Create command working directory")]
public sealed class SecretsDiscoveryTests
{
    [Theory]
    [InlineData("teams", "kits", false)]
    [InlineData("config/teams", "shared/kits", true)]
    public async Task NestedSecretsFindTheSameKitServicesAndDeclarationsAsTheOwningProjectRoot(
        string teams,
        string kits,
        bool explicitRoot
    )
    {
        // Arrange
        using var project = new SourceProjectFixture(teams, kits);
        var rootExit = await project.InvokeAsync(
            project.ProjectDirectory,
            "secrets",
            "list",
            "--squad",
            "ac"
        );
        var rootOutput = project.Console.Output.ToList();

        // Act
        var exitCode = await project.InvokeAsync(
            explicitRoot ? project.Root.Path : project.NestedDirectory,
            [
                "secrets",
                "list",
                "--squad",
                "ac",
                .. explicitRoot ? new[] { "--root", "project/src/app/deep" } : [],
            ]
        );

        // Assert
        rootExit.Should().Be(0, project.Console.AllText);
        exitCode.Should().Be(0, project.Console.AllText);
        project.Console.Output.Skip(rootOutput.Count).Should().Equal(rootOutput);
        project.Console.AllText.Should().Contain("github").And.Contain("project-kit");
        project.Console.AllText.Should().NotContain("not declared");
        await project.Sbx.Received(2).ListSecretsAsync(Arg.Any<CancellationToken>());
        project.Runner.ReceivedCalls().Should().BeEmpty();
    }
}
