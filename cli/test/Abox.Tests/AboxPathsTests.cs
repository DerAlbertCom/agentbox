using Abox.Configuration;

namespace Abox.Tests;

public sealed class AboxPathsTests
{
    [Fact]
    public void WithoutTheVariablesTheDirectoriesAreTheOnesOfThePlatform()
    {
        var paths = AboxPaths.Resolve(null, null, "/home/albert", windows: false);

        paths.ConfigDirectory.Should().Be(Path.Combine("/home/albert", ".config", "abox"));
        paths.StateDirectory.Should().Be(Path.Combine("/home/albert", ".local", "state", "abox"));
    }

    [Fact]
    public void OnWindowsTheDirectoriesAreUnderTheHome()
    {
        var paths = AboxPaths.Resolve(null, null, "C:\\Users\\albert", windows: true);

        paths.ConfigDirectory.Should().Be(Path.Combine("C:\\Users\\albert", ".abox", "config"));
        paths.StateDirectory.Should().Be(Path.Combine("C:\\Users\\albert", ".abox", "state"));
    }

    [Fact]
    public void TheVariablesAreHonoredAndCarryTheNameOfAbox()
    {
        var paths = AboxPaths.Resolve("/xdg/config", "/xdg/state", "/home/albert", windows: false);

        paths.ConfigDirectory.Should().Be(Path.Combine("/xdg/config", "abox"));
        paths.StateDirectory.Should().Be(Path.Combine("/xdg/state", "abox"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    public void AValueThatIsNoAbsolutePathCountsAsUnset(string value)
    {
        var paths = AboxPaths.Resolve(value, value, "/home/albert", windows: false);

        paths.ConfigDirectory.Should().Be(Path.Combine("/home/albert", ".config", "abox"));
        paths.StateDirectory.Should().Be(Path.Combine("/home/albert", ".local", "state", "abox"));
    }

    [Fact]
    public void TheBuildsLiveUnderTheStateDirectory()
    {
        var paths = AboxPaths.For("/config", "/state");

        paths
            .BuildDirectory("ac", "latest")
            .Should()
            .Be(Path.Combine("/state", "builds", "ac", "latest"));
    }
}
