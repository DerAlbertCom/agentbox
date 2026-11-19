using Abox.Configuration;
using Abox.Kits;

namespace Abox.Tests;

[Collection("Create command working directory")]
public sealed class SquadDiscoveryTests
{
    [Theory]
    [InlineData("teams", "kits")]
    [InlineData("config/teams", "shared/kits")]
    public void NestedDiscoveryUsesTheOwningProjectsKitsAndInheritedSharedWorkload(
        string teams,
        string kits
    )
    {
        // Arrange
        using var root = new TempDirectory();
        root.File($"{teams}/sample/ac.abox", "base: cape\nkits: [project-kit]\n");
        root.File($"{teams}/sample/cape.abox", "workload: shared\n");
        root.File($"{teams}/other/cape.abox", "workload: wrong\n");
        var workload = root.File($"{kits}/shared.workload", "template: example.com/base:1\n");
        var projectKit = root.CreateDirectory($"{kits}/project-kit");
        var personalKit = root.CreateDirectory($"{kits}/personal-kit");
        var personal = root.File("personal/extra.abox", "kits: [personal-kit]\n");
        var nested = root.CreateDirectory("projects/app/src");
        root.File("projects/app/src/kits/shared.workload", "template: example.com/wrong:1\n");
        var resolver = new SquadResolver(
            new AboxConfig { TeamsDirectory = teams, KitsDirectory = kits }
        );

        // Act
        var fromRoot = resolver.Resolve(root.Path, "sample", "ac", [personal]);
        var fromNested = resolver.Resolve(nested, "sample", "ac", [personal]);

        // Assert
        Path.GetFullPath(fromNested.Workload.Path).Should().Be(Path.GetFullPath(workload));
        fromNested.Workload.Path.Should().Be(fromRoot.Workload.Path);
        fromNested
            .Kits.Select(kit => Path.GetFullPath(kit.Directory!))
            .Should()
            .Equal(Path.GetFullPath(projectKit), Path.GetFullPath(personalKit));
        fromNested.Kits.Should().BeEquivalentTo(fromRoot.Kits);
        fromNested.RootDirectory.Should().Be(root.Path);
    }

    [Theory]
    [InlineData(".")]
    [InlineData("src")]
    public void RelativeAndAbsoluteRootsSearchTheSameParents(string sourceRoot)
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("teams/sample/ac.abox", "workload: shared\nkits: [project-kit]\n");
        root.File("kits/shared.workload", "template: example.com/base:1\n");
        root.CreateDirectory("kits/project-kit");
        var workingDirectory = root.CreateDirectory("projects/app");
        root.CreateDirectory("projects/app/src");
        var resolver = new SquadResolver(new AboxConfig());
        var originalDirectory = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(workingDirectory);

            // Act
            var relative = resolver.Resolve(sourceRoot, "sample", "ac", []);
            var absolute = resolver.Resolve(Path.GetFullPath(sourceRoot), "sample", "ac", []);
            var team = resolver.ResolveTeam(sourceRoot, "sample");

            // Assert
            relative.Should().BeEquivalentTo(absolute);
            relative.RootDirectory.Should().Be(root.Path);
            team.RootDirectory.Should().Be(root.Path);
            team.Folder.Should().Be(Path.Combine(root.Path, "teams", "sample"));
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDirectory);
        }
    }

    [Theory]
    [InlineData("kit")]
    [InlineData("workload")]
    public void MissingOwningProjectSourcesAreNotTakenFromTheInvocationFolder(string missing)
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("teams/sample/ac.abox", "workload: shared\nkits: [project-kit]\n");
        if (missing == "kit")
            root.File("kits/shared.workload", "template: example.com/base:1\n");
        else
            root.CreateDirectory("kits/project-kit");
        var nested = root.CreateDirectory("projects/app/src");
        root.File("projects/app/src/kits/shared.workload", "template: example.com/wrong:1\n");
        root.CreateDirectory("projects/app/src/kits/project-kit");
        var resolver = new SquadResolver(new AboxConfig());

        // Act
        var resolve = () => resolver.Resolve(nested, "sample", "ac", []);

        // Assert
        resolve
            .Should()
            .Throw<AboxException>()
            .WithMessage(missing == "kit" ? "*kit 'project-kit'*" : "*workload 'shared.workload'*");
    }

    [Fact]
    public void NestedDiscoveryStillRejectsAnAmbiguousCrossTeamBase()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("config/teams/sample/ac.abox", "base: shared\n");
        root.File("config/teams/one/shared.abox", "workload: one\n");
        root.File("config/teams/two/shared.abox", "workload: two\n");
        var nested = root.CreateDirectory("src/app");
        var resolver = new SquadResolver(new AboxConfig { TeamsDirectory = "config/teams" });

        // Act
        var resolve = () => resolver.Resolve(nested, "sample", "ac", []);

        // Assert
        resolve
            .Should()
            .Throw<AboxException>()
            .WithMessage("*base 'shared'*several team folders*");
    }
}
