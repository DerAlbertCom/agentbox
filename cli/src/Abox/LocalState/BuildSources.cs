using System.Security.Cryptography;
using System.Text;

namespace Abox.LocalState;

/// <summary>Fingerprints local build inputs and snapshots those exact inputs for publication.</summary>
public static class BuildSources
{
    public static Dictionary<string, string> Capture(BuildManifest manifest)
    {
        try
        {
            return CaptureInputs(manifest);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                $"Cannot fingerprint local build sources: {exception.Message}",
                exception
            );
        }
    }

    private static Dictionary<string, string> CaptureInputs(BuildManifest manifest)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var directory in Directories(manifest).Distinct(StringComparer.Ordinal))
        {
            result[Path.GetFullPath(directory)] = DirectoryHash(directory);
        }

        foreach (var file in manifest.ConfigFiles)
        {
            result[Path.GetFullPath(file)] = FileHash(file);
        }

        return result;
    }

    public static void EnsureUnchanged(BuildManifest manifest)
    {
        if (manifest.SourceHashes is not { Count: > 0 })
        {
            throw new AboxException(
                "This build has no source fingerprints. Run 'abox build --no-personal' again before publishing."
            );
        }

        var current = Capture(manifest);
        if (
            current.Count != manifest.SourceHashes.Count
            || current.Any(pair =>
                !manifest.SourceHashes.TryGetValue(pair.Key, out var hash) || hash != pair.Value
            )
        )
        {
            throw new AboxException(
                "The local build sources changed. Run 'abox build --no-personal' again before publishing."
            );
        }
    }

    public static IReadOnlyDictionary<string, string> Snapshot(
        BuildManifest manifest,
        string target
    )
    {
        EnsureUnchanged(manifest);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var source in Directories(manifest).Distinct(StringComparer.Ordinal))
        {
            var destination = Path.Combine(
                target,
                result.Count.ToString(),
                Path.GetFileName(Path.TrimEndingDirectorySeparator(source))
            );
            Directory.CreateDirectory(destination);
            var entries = Entries(source).ToList();
            foreach (var entry in entries)
            {
                var copied = Path.Combine(destination, Path.GetRelativePath(source, entry));
                if (Directory.Exists(entry))
                {
                    Directory.CreateDirectory(copied);
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(copied)!);
                File.Copy(entry, copied);
            }

            if (!OperatingSystem.IsWindows())
            {
                foreach (var entry in entries.AsEnumerable().Reverse())
                {
                    File.SetUnixFileMode(
                        Path.Combine(destination, Path.GetRelativePath(source, entry)),
                        File.GetUnixFileMode(entry)
                    );
                }
            }

            if (DirectoryHash(destination) != manifest.SourceHashes[Path.GetFullPath(source)])
            {
                throw new AboxException(
                    "The local build sources changed while snapshotting. Run 'abox build --no-personal' again."
                );
            }

            result[source] = destination;
        }

        return result;
    }

    private static IEnumerable<string> Directories(BuildManifest manifest)
    {
        yield return manifest.WorkloadKitDirectory;
        foreach (var kit in manifest.Kits.Where(kit => kit.IsLocal))
        {
            yield return kit.Reference;
        }

        if (manifest.StateMixinDirectory is { } state)
        {
            yield return state;
        }
    }

    internal static string DirectoryHash(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (
            var entry in Entries(directory)
                .OrderBy(entry => Path.GetRelativePath(directory, entry), StringComparer.Ordinal)
        )
        {
            var isDirectory = Directory.Exists(entry);
            hash.AppendData(
                Encoding.UTF8.GetBytes(
                    (isDirectory ? "D" : "F")
                        + Path.GetRelativePath(directory, entry).Replace('\\', '/')
                        + "\0"
                )
            );
            if (!OperatingSystem.IsWindows())
            {
                hash.AppendData(
                    Encoding.UTF8.GetBytes(((int)File.GetUnixFileMode(entry)).ToString() + "\0")
                );
            }

            if (!isDirectory)
            {
                hash.AppendData(Convert.FromHexString(FileHash(entry)));
            }
        }

        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private static string FileHash(string path)
    {
        RejectLink(path);
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static IEnumerable<string> Entries(string directory)
    {
        RejectLink(directory);
        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            RejectLink(entry);
            yield return entry;
            if (Directory.Exists(entry))
            {
                foreach (var file in Entries(entry))
                {
                    yield return file;
                }
            }
        }
    }

    private static void RejectLink(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new AboxException(
                $"Build input '{path}' is a symbolic link or reparse point; publication requires ordinary files and directories."
            );
        }
    }
}
