using Abox.Build;
using Abox.Configuration;

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
        _store.Save(new BuildManifest { Squad = "ac", Tag = "latest" });

        var manifest = _store.Load("ac", "latest");

        manifest.Should().NotBeNull();
        manifest!.Squad.Should().Be("ac");
        manifest.Tag.Should().Be("latest");
    }

    [Fact]
    public void ABuildThatIsNotThereIsNull()
    {
        _store.Load("ac", "latest").Should().BeNull();
    }

    [Fact]
    public void ABuildThatIsNotReadableIsAnError()
    {
        var directory = _store.DirectoryFor("ac", "latest");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "build.json"), "not json");

        var action = () => _store.Load("ac", "latest");

        action.Should().Throw<AboxException>().WithMessage("*not readable*");
    }

    [Fact]
    public void TheKitsAndStateOfABuildSurviveTheRoundTrip()
    {
        _store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
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

        var manifest = _store.Load("ac", "latest")!;

        manifest.WorkloadKitDirectory.Should().Be("/kits/workload");
        manifest.Kits.Should().ContainSingle().Which.Name.Should().Be("pi-config");
        manifest
            .State.Should()
            .ContainSingle()
            .Which.SandboxPath.Should()
            .Be("/home/agent/.config/opencode");
    }
}
