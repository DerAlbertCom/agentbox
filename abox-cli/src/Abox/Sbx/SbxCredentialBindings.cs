using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;

namespace Abox.Sbx;

/// <summary>
/// The approvals sbx keeps in its <c>credentials.yaml</c>: a binding lets a kit that is
/// not built into sbx have a credential injected for the domains it lists. This is the
/// only place that reads or writes the file; it keeps everything it does not know.
/// </summary>
public sealed class SbxCredentialBindings(string path)
{
    private const string Mechanism = "apiKey";

    /// <summary>The file sbx reads for the current user.</summary>
    public static SbxCredentialBindings Create() =>
        new(
            ResolvePath(
                Environment.GetEnvironmentVariable("APPDATA"),
                Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"),
                Environment.GetEnvironmentVariable("HOME")
                    ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                OperatingSystem.IsWindows()
            )
        );

    /// <summary>
    /// Where sbx keeps the file: <c>%APPDATA%\sbx</c> on Windows, otherwise
    /// <c>$XDG_CONFIG_HOME/sbx</c> or <c>~/.config/sbx</c>.
    /// </summary>
    public static string ResolvePath(
        string? appData,
        string? xdgConfigHome,
        string home,
        bool windows
    )
    {
        var directory =
            windows && !string.IsNullOrWhiteSpace(appData) ? appData
            : !windows && Path.IsPathRooted(xdgConfigHome) ? xdgConfigHome!
            : Path.Combine(home, ".config");
        return Path.Combine(directory, "sbx", "credentials.yaml");
    }

    /// <summary>The file the bindings are kept in.</summary>
    public string FilePath => path;

    /// <summary>Whether the binding of a service approves its API key for every domain.</summary>
    public bool Covers(string service, IReadOnlyList<string> domains)
    {
        var approved = Domains(Load(), service, create: false);
        return approved is not null
            && domains.All(domain =>
                approved.Children.Any(entry =>
                    string.Equals(Scalar(entry), domain, StringComparison.OrdinalIgnoreCase)
                )
            );
    }

    /// <summary>
    /// Approves the API key of a service for the domains. The domains are added to the
    /// binding of the service; every other entry stays as it is.
    /// </summary>
    public void Approve(string service, IReadOnlyList<string> domains)
    {
        var root = Load();
        var approved = Domains(root, service, create: true)!;
        foreach (var domain in domains)
        {
            if (
                !approved.Children.Any(entry =>
                    string.Equals(Scalar(entry), domain, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                approved.Add(new YamlScalarNode(domain));
            }
        }

        Save(root);
    }

    private YamlMappingNode Load()
    {
        string text;
        try
        {
            if (!File.Exists(path))
            {
                return new YamlMappingNode();
            }

            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new AboxException(
                $"Cannot read the credential approvals of sbx in '{path}': {exception.Message}",
                exception
            );
        }

        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (YamlException exception)
        {
            throw new AboxException(
                $"Cannot read the credential approvals of sbx in '{path}': {exception.Message}",
                exception
            );
        }

        if (stream.Documents.Count == 0)
        {
            return new YamlMappingNode();
        }

        return stream.Documents[0].RootNode switch
        {
            YamlMappingNode mapping => mapping,
            YamlScalarNode { Value: null or "" } => new YamlMappingNode(),
            _ => throw Invalid("the file"),
        };
    }

    private YamlSequenceNode? Domains(YamlMappingNode root, string service, bool create)
    {
        var bindings = Child<YamlMappingNode>(root, "bindings", "bindings", create);
        var binding = bindings is null
            ? null
            : Child<YamlMappingNode>(bindings, service, $"bindings.{service}", create);
        var mechanism = binding is null
            ? null
            : Child<YamlMappingNode>(binding, Mechanism, $"bindings.{service}.{Mechanism}", create);
        return mechanism is null
            ? null
            : Child<YamlSequenceNode>(
                mechanism,
                "domains",
                $"bindings.{service}.{Mechanism}.domains",
                create
            );
    }

    private T? Child<T>(YamlMappingNode parent, string key, string keyPath, bool create)
        where T : YamlNode, new()
    {
        if (parent.Children.TryGetValue(new YamlScalarNode(key), out var node))
        {
            return node switch
            {
                T typed => typed,
                YamlScalarNode { Value: null or "" } when create => Replace(),
                YamlScalarNode { Value: null or "" } => null,
                _ => throw Invalid($"'{keyPath}'"),
            };
        }

        return create ? Replace() : null;

        T Replace()
        {
            var child = new T();
            parent.Children[new YamlScalarNode(key)] = child;
            return child;
        }
    }

    private void Save(YamlMappingNode root)
    {
        var writer = new StringWriter();
        new YamlStream(new YamlDocument(root)).Save(writer, assignAnchors: false);
        // The emitter closes the document with an explicit end marker that sbx does not write.
        var text = writer.ToString().TrimEnd();
        if (text.EndsWith("...", StringComparison.Ordinal))
        {
            text = text[..^"...".Length].TrimEnd();
        }

        text += Environment.NewLine;

        var temporary = $"{path}.{Guid.NewGuid():n}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(temporary, text);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }

            throw new AboxException(
                $"Cannot write the credential approvals of sbx in '{path}': {exception.Message}",
                exception
            );
        }
    }

    private AboxException Invalid(string what) =>
        new(
            $"Cannot read the credential approvals of sbx in '{path}': {what} is not what sbx writes. Fix or remove the file."
        );

    private static string? Scalar(YamlNode node) => (node as YamlScalarNode)?.Value;
}
