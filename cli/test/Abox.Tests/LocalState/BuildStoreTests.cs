using Abox.Configuration;
using Abox.LocalState;

namespace Abox.Tests;

public sealed class BuildStoreTests
{
    private readonly TempDirectory _root = new();
    private readonly BuildStore _store;

    public BuildStoreTests()
    {
        _store = new BuildStore(
            AboxPaths.For(Path.Combine(_root.Path, "config"), Path.Combine(_root.Path, "state"))
        );
    }

    [Fact]
    public void ABuildIsKeptUnderItsSquadAndTag()
    {
        // Arrange
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = "agentbox-rwest-sample:latest",
            }
        );

        // Act
        var manifest = _store.Load("sample", "ac", "latest");

        // Assert
        manifest.Should().NotBeNull();
        manifest!.Squad.Should().Be("ac");
        manifest.Tag.Should().Be("latest");
    }

    [Fact]
    public void ABuildThatIsNotThereIsNull()
    {
        // Arrange: the store has no saved build.

        // Act
        var manifest = _store.Load("sample", "ac", "latest");

        // Assert
        manifest.Should().BeNull();
    }

    [Fact]
    public void ABuildThatIsNotReadableIsAnError()
    {
        // Arrange
        var directory = _store.DirectoryFor("sample", "ac", "latest");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "build.json"), "not json");

        // Act
        var action = () => _store.Load("sample", "ac", "latest");

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*not readable*");
    }

    [Fact]
    public void TheKitsAndStateOfABuildSurviveTheRoundTrip()
    {
        // Arrange
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = "agentbox-rwest-sample:latest",
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
            }
        );

        // Act
        var manifest = _store.Load("sample", "ac", "latest")!;

        // Assert
        manifest.WorkloadKitDirectory.Should().Be("/kits/workload");
        manifest.Kits.Should().ContainSingle().Which.Name.Should().Be("pi-config");
        manifest
            .State.Should()
            .ContainSingle()
            .Which.SandboxPath.Should()
            .Be("/home/agent/.config/opencode");
    }
}
