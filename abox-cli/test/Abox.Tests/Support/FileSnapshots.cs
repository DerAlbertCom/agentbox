namespace Abox.Tests;

internal static class FileSnapshots
{
    internal static Dictionary<string, byte[]> CaptureBytes(string directory) =>
        Directory
            .EnumerateFiles(directory, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

    internal static Dictionary<string, string> CaptureText(string directory) =>
        CaptureText(Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories));

    internal static Dictionary<string, string> CaptureText(IEnumerable<string> paths) =>
        paths.ToDictionary(path => path, File.ReadAllText);

    internal static void AssertUnchanged(IReadOnlyDictionary<string, byte[]> originals)
    {
        foreach (var (path, original) in originals)
            File.ReadAllBytes(path).Should().Equal(original);
    }

    internal static void AssertUnchanged(IReadOnlyDictionary<string, string> originals)
    {
        foreach (var (path, original) in originals)
            File.ReadAllText(path).Should().Be(original);
    }
}
