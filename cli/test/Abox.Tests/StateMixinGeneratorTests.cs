using Abox.Kits;

namespace Abox.Tests;

public sealed class StateMixinGeneratorTests
{
    private readonly TempDirectory _root = new();
    private readonly StateMixinGenerator _generator = new();

    [Fact]
    public void EveryStateDirectoryBecomesAHostMount()
    {
        var entries = new[]
        {
            StateEntry.Parse("~/.config/opencode/state"),
            StateEntry.Parse("~/.local/state/shell"),
        };

        _generator.Generate(entries, Path.Combine(_root.Path, "state"));

        var descriptor = File.ReadAllText(Path.Combine(_root.Path, "state", "state.yaml"));
        descriptor.Should().Contain("# syntax=docker/sandbox-kit:3");
        descriptor.Should().Contain("kind: mixin");
        CountOccurrences(descriptor, "com.docker.sandbox/host-mount@1").Should().Be(2);
        descriptor.Should().Contain("path: /home/agent/.config/opencode/state");
        descriptor.Should().Contain("path: /home/agent/.local/state/shell");
    }

    [Fact]
    public void AMixinWithoutStateHasNoCapabilities()
    {
        _generator.Generate([], Path.Combine(_root.Path, "state"));

        var descriptor = File.ReadAllText(Path.Combine(_root.Path, "state", "state.yaml"));
        descriptor.Should().NotContain("host-mount@1");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
