using Abox.Configuration;

namespace Abox.Tests;

public sealed class AboxPathsTests
{
    [Fact]
    public void WithoutTheVariablesTheDirectoriesAreTheOnesOfThePlatform()
    {
        var paths = AboxPaths.Resolve(null, null, "/home/albert", windows: false);

        Assert.Equal(Path.Combine("/home/albert", ".config", "abox"), paths.ConfigDirectory);
        Assert.Equal(Path.Combine("/home/albert", ".local", "state", "abox"), paths.StateDirectory);
    }

    [Fact]
    public void OnWindowsTheDirectoriesAreUnderTheHome()
    {
        var paths = AboxPaths.Resolve(null, null, "C:\\Users\\albert", windows: true);

        Assert.Equal(Path.Combine("C:\\Users\\albert", ".abox", "config"), paths.ConfigDirectory);
        Assert.Equal(Path.Combine("C:\\Users\\albert", ".abox", "state"), paths.StateDirectory);
    }

    [Fact]
    public void TheVariablesAreHonoredAndCarryTheNameOfAbox()
    {
        var paths = AboxPaths.Resolve("/xdg/config", "/xdg/state", "/home/albert", windows: false);

        Assert.Equal(Path.Combine("/xdg/config", "abox"), paths.ConfigDirectory);
        Assert.Equal(Path.Combine("/xdg/state", "abox"), paths.StateDirectory);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("relative/path")]
    public void AValueThatIsNoAbsolutePathCountsAsUnset(string value)
    {
        var paths = AboxPaths.Resolve(value, value, "/home/albert", windows: false);

        Assert.Equal(Path.Combine("/home/albert", ".config", "abox"), paths.ConfigDirectory);
        Assert.Equal(Path.Combine("/home/albert", ".local", "state", "abox"), paths.StateDirectory);
    }

    [Fact]
    public void TheBuildsLiveUnderTheStateDirectory()
    {
        var paths = AboxPaths.For("/config", "/state");

        Assert.Equal(Path.Combine("/state", "builds", "ac", "latest"), paths.BuildDirectory("ac", "latest"));
    }
}
