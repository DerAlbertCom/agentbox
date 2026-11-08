using Abox.Infrastructure;

namespace Abox.Kits;

/// <summary>
/// A parsed <c>.abox</c> file: the configuration of a squad, or of a base it inherits from.
/// </summary>
public sealed class AboxFile
{
    private AboxFile(
        string path,
        string? @base,
        string? workload,
        IReadOnlyList<KitDeclaration> kits,
        IReadOnlyDictionary<string, string> parameters,
        IReadOnlyList<McpDeclaration> mcp,
        IReadOnlyList<string> state,
        IReadOnlyList<SecretDeclaration> secrets
    )
    {
        Path = path;
        Base = @base;
        Workload = workload;
        Kits = kits;
        Parameters = parameters;
        Mcp = mcp;
        State = state;
        Secrets = secrets;
    }

    /// <summary>The file this configuration was read from.</summary>
    public string Path { get; }

    /// <summary>The directory the file lives in. Kits and workloads are resolved against it.</summary>
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? ".";

    /// <summary>The name of the configuration this one inherits from.</summary>
    public string? Base { get; }

    /// <summary>The name of the workload, which only the root configuration declares.</summary>
    public string? Workload { get; }

    /// <summary>The kits this configuration adds.</summary>
    public IReadOnlyList<KitDeclaration> Kits { get; }

    /// <summary>The named values this configuration declares.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; }

    /// <summary>The MCP servers this configuration adds.</summary>
    public IReadOnlyList<McpDeclaration> Mcp { get; }

    /// <summary>The sandbox directories this configuration keeps across a replacement.</summary>
    public IReadOnlyList<string> State { get; }

    /// <summary>The secrets this configuration declares.</summary>
    public IReadOnlyList<SecretDeclaration> Secrets { get; }

    /// <summary>Reads a <c>.abox</c> file.</summary>
    public static AboxFile Load(string path)
    {
        var reader = YamlReader.LoadFile(path);
        reader.RejectUnknownKeys(
            "base",
            "workload",
            "kits",
            "parameters",
            "mcp",
            "state",
            "secrets"
        );

        return new AboxFile(
            path,
            reader.GetString("base"),
            reader.GetString("workload"),
            ReadKits(reader),
            reader.GetStringMap("parameters"),
            ReadMcp(reader),
            reader.GetStringList("state"),
            ReadSecrets(reader)
        );
    }

    private static IReadOnlyList<KitDeclaration> ReadKits(YamlReader reader)
    {
        var kits = new List<KitDeclaration>();

        foreach (var entry in reader.GetEntries("kits"))
        {
            if (entry.IsScalar)
            {
                kits.Add(new KitDeclaration(entry.ScalarValue, new Dictionary<string, string>()));
                continue;
            }

            entry.RejectUnknownKeys("name", "args");
            var name =
                entry.GetString("name")
                ?? throw new AboxException($"'{reader.Source}': a kit entry needs a 'name'.");
            kits.Add(new KitDeclaration(name, entry.GetStringMap("args")));
        }

        return kits;
    }

    private static IReadOnlyList<SecretDeclaration> ReadSecrets(YamlReader reader)
    {
        var secrets = new List<SecretDeclaration>();

        foreach (var entry in reader.GetEntries("secrets"))
        {
            if (entry.IsScalar)
            {
                secrets.Add(new SecretDeclaration(entry.ScalarValue));
                continue;
            }

            entry.RejectUnknownKeys(
                "service",
                "prompt",
                "inputs",
                "separator",
                "encoding",
                "host",
                "env"
            );

            var service =
                entry.GetString("service")
                ?? throw new AboxException($"'{reader.Source}': a secret entry needs a 'service'.");

            var inputs = entry
                .GetEntries("inputs")
                .Select(input =>
                {
                    input.RejectUnknownKeys("name", "prompt");
                    var name =
                        input.GetString("name")
                        ?? throw new AboxException(
                            $"'{reader.Source}': a secret input needs a 'name'."
                        );

                    return new SecretInput(name, input.GetString("prompt"));
                })
                .ToList();

            var encoding = ReadEncoding(reader, entry.GetString("encoding"));

            secrets.Add(
                new SecretDeclaration(
                    service,
                    entry.GetString("prompt"),
                    inputs,
                    entry.GetString("separator") ?? ":",
                    encoding,
                    entry.GetString("host"),
                    entry.GetString("env")
                )
            );
        }

        return secrets;
    }

    private static SecretEncoding ReadEncoding(YamlReader reader, string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            null or "" or "plain" => SecretEncoding.Plain,
            "base64" => SecretEncoding.Base64,
            _ => throw new AboxException(
                $"'{reader.Source}': '{value}' is not a known encoding. Known encodings: plain, base64."
            ),
        };

    private static IReadOnlyList<McpDeclaration> ReadMcp(YamlReader reader)
    {
        var servers = new List<McpDeclaration>();

        foreach (var entry in reader.GetEntries("mcp"))
        {
            if (entry.IsScalar)
            {
                servers.Add(new McpDeclaration(entry.ScalarValue));
                continue;
            }

            entry.RejectUnknownKeys("name", "url", "command", "args", "headers");
            var name =
                entry.GetString("name")
                ?? throw new AboxException($"'{reader.Source}': an mcp entry needs a 'name'.");

            servers.Add(
                new McpDeclaration(
                    name,
                    entry.GetString("url"),
                    entry.GetString("command"),
                    entry.GetStringList("args"),
                    entry.GetStringMap("headers")
                )
            );
        }

        return servers;
    }
}
