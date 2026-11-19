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
