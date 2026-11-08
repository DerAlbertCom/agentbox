using Abox.Configuration;

namespace Abox.Build;

/// <summary>
/// Keeps what abox knows about the sandboxes it created: the squad they come from and
/// the build they were made of.
/// </summary>
public sealed class SandboxRecordStore
{
    private readonly AboxPaths _paths;

    public SandboxRecordStore(AboxPaths paths) => _paths = paths;

    /// <summary>The directory the records live in.</summary>
    public string RecordsDirectory => Path.Combine(_paths.StateDirectory, "sandboxes");

    /// <summary>The record of a sandbox, or null when abox did not create it.</summary>
    public SandboxRecord? Load(string sandbox)
    {
        var path = PathFor(sandbox);
        if (!File.Exists(path))
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.Deserialize<SandboxRecord>(File.ReadAllText(path));
    }

    /// <summary>Keeps the record of a sandbox.</summary>
    public void Save(string sandbox, SandboxRecord record)
    {
        Directory.CreateDirectory(RecordsDirectory);
        File.WriteAllText(PathFor(sandbox), System.Text.Json.JsonSerializer.Serialize(record));
    }

    /// <summary>Forgets a sandbox.</summary>
    public void Remove(string sandbox)
    {
        var path = PathFor(sandbox);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private string PathFor(string sandbox) => Path.Combine(RecordsDirectory, sandbox + ".json");
}
