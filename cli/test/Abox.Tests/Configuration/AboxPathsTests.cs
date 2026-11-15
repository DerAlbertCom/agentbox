using Abox.Configuration;

namespace Abox.Tests;

public sealed class AboxPathsTests
{
    [Fact]
    public void WithoutTheVariablesTheDirectoriesAreTheOnesOfThePlatform()
    {
        // Arrange: no directory overrides are supplied.

        // Act
        var paths = AboxPaths.Resolve(null, null, "/home/albert", windows: false);

        // Assert
        paths.ConfigDirectory.Should().Be(Path.Combine("/home/albert", ".config", "abox"));
        paths.StateDirectory.Should().Be(Path.Combine("/home/albert", ".local", "state", "abox"));
    }

    [Fact]
    public void OnWindowsTheDirectoriesAreUnderTheHome()
    {
        // Arrange: use Windows home-directory semantics without overrides.

        // Act
        var paths = AboxPaths.Resolve(null, null, "C:\\Users\\albert", windows: true);

        // Assert
        paths.ConfigDirectory.Should().Be(Path.Combine("C:\\Users\\albert", ".abox", "config"));
        paths.StateDirectory.Should().Be(Path.Combine("C:\\Users\\albert", ".abox", "state"));
    }

    [Fact]
    public void TheVariablesAreHonoredAndCarryTheNameOfAbox()
    {
        // Arrange: both XDG directory overrides are absolute.

        // Act
        var paths = AboxPaths.Resolve("/xdg/config", "/xdg/state", "/home/albert", windows: false);

        // Assert
        paths.ConfigDirectory.Should().Be(Path.Combine("/xdg/config", "abox"));
        paths.StateDirectory.Should().Be(Path.Combine("/xdg/state", "abox"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    public void AValueThatIsNoAbsolutePathCountsAsUnset(string value)
    {
        // Arrange: the theory supplies an unusable override.

        // Act
        var paths = AboxPaths.Resolve(value, value, "/home/albert", windows: false);

        // Assert
        paths.ConfigDirectory.Should().Be(Path.Combine("/home/albert", ".config", "abox"));
        paths.StateDirectory.Should().Be(Path.Combine("/home/albert", ".local", "state", "abox"));
    }

    [Fact]
    public void TheBuildsLiveUnderTheStateDirectory()
    {
        // Arrange
        var paths = AboxPaths.For("/config", "/state");

        // Act
        var directory = paths.BuildDirectory("ac", "latest");

        // Assert
        directory.Should().Be(Path.Combine("/state", "builds", "ac", "latest"));
    }
}
