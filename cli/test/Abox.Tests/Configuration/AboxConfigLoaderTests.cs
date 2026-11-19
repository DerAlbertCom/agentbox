using Abox.Configuration;

namespace Abox.Tests;

public sealed class AboxConfigLoaderTests
{
    private readonly TempDirectory _root = new();

    [Fact]
    public void AMissingFileLoadsWithoutError()
    {
        // Arrange
        var paths = AboxPaths.For(
            Path.Combine(_root.Path, "config"),
            Path.Combine(_root.Path, "state")
        );

        // Act
        Action load = () => AboxConfigLoader.Load(paths);

        // Assert
        load.Should().NotThrow();
    }

    [Fact]
    public void TheFileIsRead()
    {
        // Arrange
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

        // Act
        var loaded = AboxConfigLoader.Load(
            AboxPaths.For(config, Path.Combine(_root.Path, "state"))
        );

        // Assert
        loaded.DefaultTeam.Should().Be("ac");
        loaded.DefaultLocation.Should().Be("/home/albert/projects");
        loaded.KitsDirectory.Should().Be("kits");
        loaded.DefaultTag.Should().Be("dev");
        loaded.PullRepository.Should().Be("team-docker-release");
    }

    [Fact]
    public void AKeyThatIsNotKnownIsAnError()
    {
        // Arrange
        var config = Path.Combine(_root.Path, "config");
        Directory.CreateDirectory(config);
        File.WriteAllText(Path.Combine(config, "config.yaml"), "defaulSquad: ac\n");

        // Act
        var action = () =>
            AboxConfigLoader.Load(AboxPaths.For(config, Path.Combine(_root.Path, "state")));

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*not a known key*");
    }

    [Theory]
    [InlineData("defaultTeam: ac")]
    [InlineData("pullRepository:")]
    [InlineData("pullRepository: '   '")]
    public void AnOmittedOrBlankPullRepositoryUsesTheDefault(string yaml)
    {
        // Arrange
        _root.File("config/config.yaml", yaml);

        // Act
        var config = AboxConfigLoader.Load(
            AboxPaths.For(Path.Combine(_root.Path, "config"), _root.Path)
        );

        // Assert
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
        // Arrange
        _root.File("config/config.yaml", $"pullRepository: '{repository}'\n");

        // Act
        var action = () =>
            AboxConfigLoader.Load(AboxPaths.For(Path.Combine(_root.Path, "config"), _root.Path));

        // Assert
        action.Should().Throw<AboxException>().WithMessage("*pullRepository*repository name*");
    }
}
