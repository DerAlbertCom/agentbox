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

        config.DefaultTeam.Should().BeNull();
        config.KitsDirectory.Should().Be("kits");
        config.DefaultTag.Should().Be("latest");
        config.DefaultLocation.Should().BeNull();
        config.PullRepository.Should().Be("mtiae-docker-release");
    }

    [Fact]
    public void TheFileIsRead()
    {
        var config = Path.Combine(_root.Path, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(
            Path.Combine(config, "config.yaml"),
            """
            defaultTeam: ac
            defaultLocation: /home/albert/projects
            kitsDirectory: kits
            defaultTag: dev
            pullRepository: team-docker-release
            """
        );

        var loaded = AboxConfigLoader.Load(
            AboxPaths.For(config, Path.Combine(_root.Path, "state"))
        );

        loaded.DefaultTeam.Should().Be("ac");
        loaded.DefaultLocation.Should().Be("/home/albert/projects");
        loaded.KitsDirectory.Should().Be("kits");
        loaded.DefaultTag.Should().Be("dev");
        loaded.PullRepository.Should().Be("team-docker-release");
    }

    [Fact]
    public void AKeyThatIsNotKnownIsAnError()
    {
        var config = Path.Combine(_root.Path, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "config.yaml"), "defaulSquad: ac\n");

        var action = () =>
            AboxConfigLoader.Load(AboxPaths.For(config, Path.Combine(_root.Path, "state")));

        action.Should().Throw<AboxException>().WithMessage("*not a known key*");
    }

    [Theory]
    [InlineData("defaultTeam: ac")]
    [InlineData("pullRepository:")]
    [InlineData("pullRepository: '   '")]
    public void AnOmittedOrBlankPullRepositoryUsesTheDefault(string yaml)
    {
        _root.File("config/config.yaml", yaml);

        var config = AboxConfigLoader.Load(
            AboxPaths.For(Path.Combine(_root.Path, "config"), _root.Path)
        );

        config.PullRepository.Should().Be("mtiae-docker-release");
    }

    [Theory]
    [InlineData("https://rwest.jfrog.io/release")]
    [InlineData("release/images")]
    [InlineData("release:latest")]
    [InlineData("Release")]
    [InlineData("release images")]
    public void AnInvalidPullRepositoryIsAConfigurationError(string repository)
    {
        _root.File("config/config.yaml", $"pullRepository: '{repository}'\n");

        var action = () =>
            AboxConfigLoader.Load(AboxPaths.For(Path.Combine(_root.Path, "config"), _root.Path));

        action.Should().Throw<AboxException>().WithMessage("*pullRepository*repository name*");
    }
}
