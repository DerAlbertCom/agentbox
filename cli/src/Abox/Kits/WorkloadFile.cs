using Abox.Infrastructure;

namespace Abox.Kits;

/// <summary>
/// A parsed <c>.workload</c> file: the tools and scripts the base image is built from.
/// </summary>
public sealed class WorkloadFile
{
    /// <summary>The image a workload builds on when it does not name one.</summary>
    public const string DefaultTemplate = "docker.io/sbx/shell-docker-image";

    private WorkloadFile(
        string path,
        string template,
        IReadOnlyList<string> mise,
        IReadOnlyList<string> scripts)
    {
        Path = path;
        Template = template;
        Mise = mise;
        Scripts = scripts;
    }

    /// <summary>The file this workload was read from.</summary>
    public string Path { get; }

    /// <summary>The directory the file lives in. Scripts are resolved against it.</summary>
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? ".";

    /// <summary>The name of the workload, without the file extension.</summary>
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);

    /// <summary>The image the workload builds on.</summary>
    public string Template { get; }

    /// <summary>The tools, installed with mise. The last one is the default.</summary>
    public IReadOnlyList<string> Mise { get; }

    /// <summary>The scripts that run while the image is built.</summary>
    public IReadOnlyList<string> Scripts { get; }

    /// <summary>Reads a <c>.workload</c> file.</summary>
    public static WorkloadFile Load(string path)
    {
        var reader = YamlReader.LoadFile(path);
        reader.RejectUnknownKeys("template", "mise", "scripts");

        var template = reader.GetString("template");
        return new WorkloadFile(
            path,
            string.IsNullOrWhiteSpace(template) ? DefaultTemplate : template,
            reader.GetStringList("mise"),
            reader.GetStringList("scripts"));
    }
}
