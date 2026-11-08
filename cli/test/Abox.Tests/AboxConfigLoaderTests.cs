using Abox.Configuration;

namespace Abox.Tests;

public sealed class AboxConfigLoaderTests
{
    private readonly TempDirectory _root = new();

    [Fact]
    public void WithoutAFileTheDefaultsAreUsed()
    {
        var paths = AboxPaths.For(
            Path.Combine(_root.Path, "config"),
            Path.Combine(_root.Path, "state")
        );

        var config = AboxConfigLoader.Load(paths);

        Assert.Equal("default-squad", config.DefaultSquad);
        Assert.Equal("kits", config.KitsDirectory);
        Assert.Equal("latest", config.DefaultTag);
        Assert.Null(config.DefaultLocation);
    }

    [Fact]
    public void TheFileIsRead()
    {
        var config = Path.Combine(_root.Path, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(
            Path.Combine(config, "config.yaml"),
            """
            defaultSquad: ac
            defaultLocation: /home/albert/projects
            kitsDirectory: kits
            defaultTag: dev
            """
        );

        var loaded = AboxConfigLoader.Load(
            AboxPaths.For(config, Path.Combine(_root.Path, "state"))
        );

        Assert.Equal("ac", loaded.DefaultSquad);
        Assert.Equal("/home/albert/projects", loaded.DefaultLocation);
        Assert.Equal("kits", loaded.KitsDirectory);
        Assert.Equal("dev", loaded.DefaultTag);
    }

    [Fact]
    public void AKeyThatIsNotKnownIsAnError()
    {
        var config = Path.Combine(_root.Path, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "config.yaml"), "defaulSquad: ac\n");

        var exception = Assert.Throws<AboxException>(() =>
            AboxConfigLoader.Load(AboxPaths.For(config, Path.Combine(_root.Path, "state")))
        );

        Assert.Contains("not a known key", exception.Message);
    }
}
