using Abox.Configuration;
using Abox.LocalState;

namespace Abox.Tests;

public sealed class BuildTagSafetyTests
{
    [Theory]
    [MemberData(nameof(InvalidTags))]
    public void BuildStoreRejectsUnsafeTagsBeforeAccessingAnExternalManifest(
        string operation,
        string input
    )
    {
        // Arrange
        using var root = new TempDirectory();
        using var outside = new TempDirectory();
        var sentinel = outside.File("build.json", "external manifest must remain untouched");
        var paths = AboxPaths.For(
            Path.Combine(root.Path, "config"),
            Path.Combine(root.Path, "state")
        );
        var store = new BuildStore(paths);
        var tag = input switch
        {
            "traversal" => Path.GetRelativePath(
                Path.Combine(paths.BuildsDirectory, "ac"),
                outside.Path
            ),
            "absolute" => outside.Path,
            "overlong" => new string('a', 129),
            _ => input,
        };
        Action access = operation switch
        {
            "directory" => () => store.DirectoryFor("sample", "ac", tag),
            "load" => () => store.Load("sample", "ac", tag),
            _ => () => store.Save(new BuildManifest { Squad = "ac", Tag = tag }),
        };

        // Act
        var result = access.Should().Throw<AboxException>();

        // Assert
        result.WithMessage("*not a valid registry tag*");
        File.ReadAllText(sentinel).Should().Be("external manifest must remain untouched");
        Directory.GetFiles(outside.Path, "*", SearchOption.AllDirectories).Should().Equal(sentinel);
        Directory.Exists(paths.StateDirectory).Should().BeFalse();
    }

    public static IEnumerable<object[]> InvalidTags()
    {
        foreach (
            var tag in new[]
            {
                "traversal",
                "absolute",
                "",
                "overlong",
                "a/b",
                "a\\b",
                "..",
                "C:relative",
            }
        )
        foreach (var operation in new[] { "directory", "load", "save" })
        {
            yield return [operation, tag];
        }
    }
}
