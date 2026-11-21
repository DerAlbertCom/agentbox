using Abox.LocalState;

namespace Abox.Tests;

/// <summary>Writes explicitly selected publication inputs before capturing their provenance.</summary>
internal static class PublicationBuilds
{
    internal static string SaveWorkload(
        BuildStore store,
        BuildManifest manifest,
        string descriptorPath,
        string descriptor
    )
    {
        Directory.CreateDirectory(Path.GetDirectoryName(descriptorPath)!);
        File.WriteAllText(descriptorPath, descriptor);
        SaveVerified(store, manifest);
        return descriptorPath;
    }

    internal static void SaveVerified(BuildStore store, BuildManifest manifest)
    {
        manifest.IncludesPersonalConfigurations = false;
        manifest.SourceHashes = BuildSources.Capture(manifest);
        store.Save(manifest);
    }
}
