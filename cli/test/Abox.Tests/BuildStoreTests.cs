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

        Assert.NotNull(manifest);
        Assert.Equal("ac", manifest!.Squad);
        Assert.Equal("latest", manifest.Tag);
    }

    [Fact]
    public void ABuildThatIsNotThereIsNull()
    {
        Assert.Null(_store.Load("ac", "latest"));
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

        Assert.Equal("/kits/workload", manifest.WorkloadKitDirectory);
        Assert.Equal("pi-config", Assert.Single(manifest.Kits).Name);
        Assert.Equal("/home/agent/.config/opencode", Assert.Single(manifest.State).SandboxPath);
    }
}
