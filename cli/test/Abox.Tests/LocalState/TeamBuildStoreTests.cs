using System.Text.Json;
using Abox.Configuration;
using Abox.LocalState;
using Abox.Naming;

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
                Reference = "agentic-box-rwest-sample:latest",
                Template = "legacy-template",
            }
        );
        var legacy = root.File("builds/ac/latest/build.json", original);

        // Act
        var old = store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;
        store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = "agentic-box-rwest-sample:latest",
                Template = "new-template",
            }
        );
        var current = store.Load(new SquadBuildIdentity("SAMPLE", "AC", "latest"))!;
        var unrelated = () => store.Load(new SquadBuildIdentity("other", "ac", "latest"));

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
                    Reference = "agentic-box-rwest-sample:latest",
                }
            )
        );

        // Act
        var load = () => store.Load(new SquadBuildIdentity("sample", "ac", "latest"));

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
            {"Squad":"ac", "Tag":"latest", "Reference":"agentic-box-rwest-sample:latest"}
            """
        );
        root.File(
            "builds/sample/ac/latest/build.json",
            """
            {"Squad":"ac", "Tag":"latest", "Reference":"agentic-box-rwest-other:latest"}
            """
        );

        // Act
        var load = () => store.Load(new SquadBuildIdentity("sample", "ac", "latest"));

        // Assert
        load.Should()
            .Throw<AboxException>()
            .WithMessage("*abox build --team sample --tag latest*");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData(
        "{\"Squad\":\"ac\",\"Tag\":\"latest\",\"Reference\":\"agentic-box-rwest-sample:latest\",\"Reference\":\"agentic-box-rwest-other:latest\"}"
    )]
    public void UnreadableQualifiedBuildsNeverFallBackOrChangeLegacyArtifacts(string qualified)
    {
        // Arrange
        using var root = new TempDirectory();
        var store = new BuildStore(AboxPaths.For(root.Path, root.Path));
        const string original =
            "{\"Squad\":\"ac\",\"Tag\":\"latest\",\"Reference\":\"agentic-box-rwest-sample:latest\"}";
        var legacyPath = root.File("builds/ac/latest/build.json", original);
        var qualifiedPath = root.File("builds/sample/ac/latest/build.json", qualified);

        // Act
        var load = () => store.Load(new SquadBuildIdentity("sample", "ac", "latest"));

        // Assert
        load.Should()
            .Throw<AboxException>()
            .WithMessage("*abox build --team sample --tag latest*");
        File.ReadAllText(legacyPath).Should().Be(original);
        File.ReadAllText(qualifiedPath).Should().Be(qualified);
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
            {"Squad":"ac", "Tag":"latest", "Reference":"agentic-box-rwest-other:latest", "Reference":"agentic-box-rwest-sample:latest"}
            """
        );

        // Act
        var load = () => store.Load(new SquadBuildIdentity("sample", "ac", "latest"));

        // Assert
        load.Should()
            .Throw<AboxException>()
            .WithMessage("*abox build --team sample --tag latest*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("agentic-box-rwest-other:latest")]
    [InlineData("agentic-box-rwest-sample:other-tag")]
    [InlineData("agentic-box-rwest-sample/other:latest")]
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
        var load = () => store.Load(new SquadBuildIdentity("sample", "ac", "latest"));

        // Assert
        load.Should()
            .Throw<AboxException>()
            .WithMessage("*abox build --team sample --tag latest*");
        File.ReadAllText(legacy).Should().Be(original);
        Directory.Exists(Path.Combine(paths.BuildsDirectory, "sample")).Should().BeFalse();
    }
}
