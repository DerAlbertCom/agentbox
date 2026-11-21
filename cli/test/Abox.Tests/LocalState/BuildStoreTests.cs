using Abox.Configuration;
using Abox.LocalState;
using Abox.Naming;

namespace Abox.Tests;

public sealed class BuildStoreTests
{
    [Fact]
    public void ABuildIsKeptUnderItsSquadAndTag()
    {
        // Arrange
        using var root = new TempDirectory();
        var store = Store(root);
        store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = "agentic-box-rwest-sample:latest",
            }
        );

        // Act
        var manifest = store.Load(new SquadBuildIdentity("sample", "ac", "latest"));

        // Assert
        manifest.Should().NotBeNull();
        manifest!.Squad.Should().Be("ac");
        manifest.Tag.Should().Be("latest");
    }

    [Fact]
    public void ABuildThatIsNotThereIsNull()
    {
        // Arrange: the store has no saved build.
        using var root = new TempDirectory();
        var store = Store(root);

        // Act
        var manifest = store.Load(new SquadBuildIdentity("sample", "ac", "latest"));

        // Assert
        manifest.Should().BeNull();
    }

    [Fact]
    public void ABuildThatIsNotReadableIsAnError()
    {
        // Arrange
        using var root = new TempDirectory();
        var store = Store(root);
        var directory = store.DirectoryFor(new SquadBuildIdentity("sample", "ac", "latest"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "build.json"), "not json");

        // Act
        var action = () => store.Load(new SquadBuildIdentity("sample", "ac", "latest"));

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*not readable*");
    }

    [Fact]
    public void TheKitsAndStateOfABuildSurviveTheRoundTrip()
    {
        // Arrange
        using var root = new TempDirectory();
        var store = Store(root);
        store.Save(ManifestWithKitsAndState());

        // Act
        var manifest = store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;

        // Assert
        manifest.WorkloadKitDirectory.Should().Be("/kits/workload");
        manifest.Kits.Should().ContainSingle().Which.Name.Should().Be("pi-config");
        manifest
            .State.Should()
            .ContainSingle()
            .Which.SandboxPath.Should()
            .Be("/home/agent/.config/opencode");
    }

    private static BuildStore Store(TempDirectory root) =>
        new(AboxPaths.For(Path.Combine(root.Path, "config"), Path.Combine(root.Path, "state")));

    private static BuildManifest ManifestWithKitsAndState() =>
        new()
        {
            Squad = "ac",
            Tag = "latest",
            Reference = "agentic-box-rwest-sample:latest",
            WorkloadKitDirectory = "/kits/workload",
            Kits =
            [
                new ManifestKit
                {
                    Name = "pi-config",
                    Reference = "/kits/pi-config",
                    IsLocal = true,
                },
            ],
            State =
            [
                new ManifestState
                {
                    SandboxPath = "/home/agent/.config/opencode",
                    RelativePath = ".config/opencode",
                },
            ],
        };
}
