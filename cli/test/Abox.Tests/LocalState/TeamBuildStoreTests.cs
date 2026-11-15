using System.Text.Json;
using Abox.Configuration;
using Abox.LocalState;

namespace Abox.Tests;

public sealed class TeamBuildStoreTests
{
    [Fact]
    public void ProvenLegacyBuildsRemainReadableWithoutMigrationAndQualifiedBuildsTakePrecedence()
    {
        // Arrange
        using var root = new TempDirectory();
        var store = new BuildStore(AboxPaths.For(root.Path, root.Path));
        var original = JsonSerializer.Serialize(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = "agentbox-rwest-sample:latest",
                Template = "legacy-template",
            }
        );
        var legacy = root.File("builds/ac/latest/build.json", original);

        // Act
        var old = store.Load("sample", "ac", "latest")!;
        store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = "agentbox-rwest-sample:latest",
                Template = "new-template",
            }
        );
        var current = store.Load("SAMPLE", "AC", "latest")!;
        var unrelated = () => store.Load("other", "ac", "latest");

        // Assert
        old.Template.Should().Be("legacy-template");
        current.Template.Should().Be("new-template");
        unrelated
            .Should()
            .Throw<AboxException>()
            .WithMessage("*abox build --team other --tag latest*");
        File.ReadAllText(legacy).Should().Be(original);
    }

    [Theory]
    [InlineData("other", "latest")]
    [InlineData("ac", "release")]
    public void ALegacyOwnershipMarkerDoesNotOverrideConflictingSquadOrTag(string squad, string tag)
    {
        // Arrange
        using var root = new TempDirectory();
        var store = new BuildStore(AboxPaths.For(root.Path, root.Path));
        root.File(
            "builds/ac/latest/build.json",
            JsonSerializer.Serialize(
                new BuildManifest
                {
                    Squad = squad,
                    Tag = tag,
                    Reference = "agentbox-rwest-sample:latest",
                }
            )
        );

        // Act
        var load = () => store.Load("sample", "ac", "latest");

        // Assert
        load.Should()
            .Throw<AboxException>()
            .WithMessage("*abox build --team sample --tag latest*");
    }

    [Fact]
    public void InvalidQualifiedOwnershipNeverFallsBackToAnOwnedLegacyBuild()
    {
        // Arrange
        using var root = new TempDirectory();
        var store = new BuildStore(AboxPaths.For(root.Path, root.Path));
        root.File(
            "builds/ac/latest/build.json",
            """
            {"Squad":"ac", "Tag":"latest", "Reference":"agentbox-rwest-sample:latest"}
            """
        );
        root.File(
            "builds/sample/ac/latest/build.json",
            """
            {"Squad":"ac", "Tag":"latest", "Reference":"agentbox-rwest-other:latest"}
            """
        );

        // Act
        var load = () => store.Load("sample", "ac", "latest");

        // Assert
        load.Should()
            .Throw<AboxException>()
            .WithMessage("*abox build --team sample --tag latest*");
    }

    [Fact]
    public void ConflictingLegacyOwnershipMarkersAreNotGuessedFromTheLastValue()
    {
        // Arrange
        using var root = new TempDirectory();
        var store = new BuildStore(AboxPaths.For(root.Path, root.Path));
        root.File(
            "builds/ac/latest/build.json",
            """
            {"Squad":"ac", "Tag":"latest", "Reference":"agentbox-rwest-other:latest", "Reference":"agentbox-rwest-sample:latest"}
            """
        );

        // Act
        var load = () => store.Load("sample", "ac", "latest");

        // Assert
        load.Should()
            .Throw<AboxException>()
            .WithMessage("*abox build --team sample --tag latest*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("agentbox-rwest-other:latest")]
    [InlineData("agentbox-rwest-sample:other-tag")]
    [InlineData("agentbox-rwest-sample/other:latest")]
    public void UnprovenLegacyOwnershipRequiresARequestedTeamRebuildWithoutChangingArtifacts(
        string? reference
    )
    {
        // Arrange
        using var root = new TempDirectory();
        var paths = AboxPaths.For(root.Path, root.Path);
        var store = new BuildStore(paths);
        var original = JsonSerializer.Serialize(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = reference,
            }
        );
        var legacy = root.File("builds/ac/latest/build.json", original);

        // Act
        var load = () => store.Load("sample", "ac", "latest");

        // Assert
        load.Should()
            .Throw<AboxException>()
            .WithMessage("*abox build --team sample --tag latest*");
        File.ReadAllText(legacy).Should().Be(original);
        Directory.Exists(Path.Combine(paths.BuildsDirectory, "sample")).Should().BeFalse();
    }
}
