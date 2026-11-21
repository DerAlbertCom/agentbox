using Abox.Infrastructure;
using Abox.Kits;
using static Abox.Tests.TestText;

namespace Abox.Tests;

public sealed class StateMixinGeneratorTests
{
    private readonly TempDirectory _root = new();
    private readonly StateMixinGenerator _generator = new();

    [Fact]
    public void EveryStateDirectoryBecomesAHostMount()
    {
        // Arrange
        var entries = new[]
        {
            StateEntry.Parse("~/.config/opencode/state"),
            StateEntry.Parse("~/.local/state/shell"),
            StateEntry.Parse("~/a #b/é雪'😀"),
            StateEntry.Parse("~/[cache]/{data}/&sessions/!history"),
            // Scalar escaping is independent of the portable-path validation exercised above.
            new StateEntry(
                "/quote\"/slash\\/\0\t\n\r\u001f\u007f\u0085\u009f\u2028\u2029\uFEFF",
                "unused"
            ),
        };

        // Act
        _generator.Generate(entries, Path.Combine(_root.Path, "state"));

        // Assert
        var descriptor = File.ReadAllText(Path.Combine(_root.Path, "state", "state.yaml"));
        descriptor.Should().NotContain("\r").And.Contain("path: \"").And.NotContain("\\uD83D");
        CountOccurrences(descriptor, "com.docker.sandbox/host-mount@1").Should().Be(entries.Length);
        YamlReader
            .Parse(descriptor, "generated state")
            .GetEntries("capabilities")
            .Select(capability => capability.GetEntry("config")!.GetString("path"))
            .Should()
            .Equal(entries.Select(entry => entry.SandboxPath));
    }

    [Fact]
    public void TheHomeStateDirectoriesAndTheirParentsBelongToTheSandboxUser()
    {
        // Arrange: sbx creates a missing mount point and its parents as root.
        var entries = new[]
        {
            StateEntry.Parse("~/.config/opencode/state"),
            StateEntry.Parse("/var/cache/tool"),
        };
        var target = Path.Combine(_root.Path, "state");

        // Act
        _generator.Generate(entries, target);

        // Assert
        File.ReadAllText(Path.Combine(target, "state.dockerfile"))
            .Should()
            .Be("FROM scratch\nCOPY --chown=1000:1000 files/home/ /home/agent/\n");
        Directory
            .Exists(Path.Combine(target, "files", "home", ".config", "opencode", "state"))
            .Should()
            .BeTrue();
        Directory.EnumerateFileSystemEntries(Path.Combine(target, "files")).Should().HaveCount(1);
    }

    [Fact]
    public void GeneratingAgainDropsTheDirectoriesOfRemovedState()
    {
        // Arrange
        var target = Path.Combine(_root.Path, "state");
        _generator.Generate([StateEntry.Parse("~/.local/state/shell")], target);

        // Act
        _generator.Generate([StateEntry.Parse("~/.local/state/sbox")], target);

        // Assert
        Directory
            .EnumerateDirectories(Path.Combine(target, "files", "home", ".local", "state"))
            .Select(Path.GetFileName)
            .Should()
            .Equal("sbox");
    }

    [Fact]
    public void AMixinWithoutHomeStateHasNoImage()
    {
        // Arrange
        var target = Path.Combine(_root.Path, "state");
        _generator.Generate([StateEntry.Parse("~/.local/state/shell")], target);

        // Act
        _generator.Generate([StateEntry.Parse("/var/cache/tool")], target);

        // Assert
        File.Exists(Path.Combine(target, "state.dockerfile")).Should().BeFalse();
        Directory.Exists(Path.Combine(target, "files")).Should().BeFalse();
    }

    [Fact]
    public void AMixinWithoutStateHasNoCapabilities()
    {
        // Arrange: the generator receives no state entries.

        // Act
        _generator.Generate([], Path.Combine(_root.Path, "state"));

        // Assert
        var descriptor = File.ReadAllText(Path.Combine(_root.Path, "state", "state.yaml"));
        descriptor.Should().NotContain("host-mount@1");
    }
}
