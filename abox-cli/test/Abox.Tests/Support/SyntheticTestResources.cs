namespace Abox.Tests;

/// <summary>Read-only synthetic inputs shipped with the executable, independent of the checkout.</summary>
internal static class SyntheticTestResources
{
    public static string File(string relativePath)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath);
        if (!System.IO.File.Exists(path))
            throw new FileNotFoundException(
                $"Synthetic test resource is missing: {path}. Rebuild Abox.Tests and retain its Fixtures directory alongside the executable.",
                path
            );
        return path;
    }
}
