using System.Text.Json;
using System.Text.RegularExpressions;
using Abox.Infrastructure;

namespace Abox.Sbx;

/// <summary>
/// Every call abox makes to <c>sbx</c>. Keeping them in one place keeps the flags and the
/// version check in one place too.
/// </summary>
public sealed partial class SbxClient : ISbxClient
{
    /// <summary>The first sbx version with v3 kits.</summary>
    public const string MinimumVersion = "0.45.0";

    private const string ExecutableName = "sbx";

    private readonly IProcessRunner _runner;

    public SbxClient(IProcessRunner runner) => _runner = runner;

    /// <summary>The version of the installed sbx.</summary>
    public async Task<Version> GetVersionAsync(CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            new ProcessRequest(ExecutableName, ["version"]),
            cancellationToken
        );
        if (!result.Succeeded)
        {
            throw new AboxException($"'sbx version' failed: {Describe(result)}");
        }

        var match = VersionPattern().Match(result.StandardOutput);
        if (!match.Success)
        {
            throw new AboxException(
                $"Cannot read the sbx version from '{result.StandardOutput.Trim()}'."
            );
        }

        return new Version(
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value)
        );
    }

    /// <summary>The report of <c>sbx diagnose</c>.</summary>
    public async Task<DiagnoseReport> DiagnoseAsync(CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            new ProcessRequest(ExecutableName, ["diagnose", "--format", "json"]),
            cancellationToken
        );
        if (!result.Succeeded)
        {
            throw new AboxException($"'sbx diagnose' failed: {Describe(result)}");
        }

        return ParseDiagnostics(result.StandardOutput);
    }

    /// <summary>The sandboxes sbx knows about.</summary>
    public async Task<IReadOnlyList<SandboxInfo>> ListSandboxesAsync(
        CancellationToken cancellationToken
    )
    {
        var result = await _runner.RunAsync(
            new ProcessRequest(ExecutableName, ["ls", "--json"]),
            cancellationToken
        );
        if (!result.Succeeded)
        {
            throw new AboxException($"'sbx ls' failed: {Describe(result)}");
        }

        return ParseSandboxes(result.StandardOutput);
    }

    /// <summary>Whether a sandbox with this name exists.</summary>
    public async Task<SandboxInfo?> FindSandboxAsync(
        string name,
        CancellationToken cancellationToken
    )
    {
        var sandboxes = await ListSandboxesAsync(cancellationToken);
        return sandboxes.FirstOrDefault(sandbox =>
            sandbox.Name.Equals(name, StringComparison.Ordinal)
        );
    }

    /// <summary>Creates a sandbox from a workload kit and its mixins.</summary>
    public async Task<SbxOutcome> CreateAsync(
        SbxCreateRequest request,
        CancellationToken cancellationToken
    )
    {
        var arguments = new List<string>
        {
            "create",
            request.KitSource,
            request.Location,
            "--name",
            request.Name,
        };

        foreach (var mixin in request.Mixins)
        {
            arguments.Add("--kit");
            arguments.Add(mixin);
        }

        foreach (var (name, value) in request.KitArguments)
        {
            arguments.Add("--kit-arg");
            arguments.Add($"{name}={value}");
        }

        foreach (var mount in request.Mounts)
        {
            arguments.Add("--mount");
            arguments.Add(mount.ToString());
        }

        if (request.StaticMcpServers.Count > 0)
        {
            arguments.Add("--static-mcp");
            arguments.Add(string.Join(",", request.StaticMcpServers));
        }

        var result = await _runner.RunAsync(
            new ProcessRequest(ExecutableName, arguments),
            cancellationToken
        );
        return new SbxOutcome(result.ExitCode, result.StandardOutput + result.StandardError);
    }

    /// <summary>Removes a sandbox without asking.</summary>
    public async Task<int> RemoveSandboxAsync(string name, CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            new ProcessRequest(ExecutableName, ["rm", name, "--force"]),
            cancellationToken
        );
        return result.ExitCode;
    }

    /// <summary>Runs a command in a sandbox, starting the sandbox when it is stopped.</summary>
    public async Task<int> ExecAsync(SbxExecRequest request, CancellationToken cancellationToken)
    {
        var arguments = new List<string> { "exec" };

        if (request.KeepStandardInputOpen)
        {
            arguments.Add("-i");
        }

        if (request.AllocateTty)
        {
            arguments.Add("-t");
        }

        if (request.User is not null)
        {
            arguments.Add("-u");
            arguments.Add(request.User);
        }

        if (request.WorkingDirectory is not null)
        {
            arguments.Add("-w");
            arguments.Add(request.WorkingDirectory);
        }

        arguments.Add(request.Sandbox);
        arguments.AddRange(request.Command);

        var result = await _runner.RunAsync(
            new ProcessRequest(ExecutableName, arguments, AttachConsole: true),
            cancellationToken
        );

        return result.ExitCode;
    }

    /// <summary>Whether a command exists inside a sandbox.</summary>
    public async Task<bool> HasProgramAsync(
        string sandbox,
        string program,
        CancellationToken cancellationToken
    )
    {
        var result = await _runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                ["exec", sandbox, "sh", "-c", $"command -v {program}"]
            ),
            cancellationToken
        );

        return result.Succeeded;
    }

    /// <summary>Where the runtime put the workspace of the sandbox.</summary>
    public async Task<string?> GetWorkspaceDirectoryAsync(
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        var result = await _runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                ["exec", sandbox, "sh", "-c", "printf %s \"$WORKSPACE_DIR\""]
            ),
            cancellationToken
        );

        var directory = result.StandardOutput.Trim();
        return string.IsNullOrEmpty(directory) ? null : directory;
    }

    /// <summary>Builds a kit from source and reports what it found.</summary>
    public async Task<(int ExitCode, string Output)> InspectKitAsync(
        string kitDirectory,
        CancellationToken cancellationToken
    )
    {
        var result = await _runner.RunAsync(
            new ProcessRequest(ExecutableName, ["kit", "inspect", kitDirectory]),
            cancellationToken
        );
        return (result.ExitCode, result.StandardOutput + result.StandardError);
    }

    /// <summary>The MCP servers registered on the host.</summary>
    public async Task<IReadOnlyList<string>> ListMcpServersAsync(
        CancellationToken cancellationToken
    )
    {
        var result = await _runner.RunAsync(
            new ProcessRequest(ExecutableName, ["mcp", "ls", "--quiet"]),
            cancellationToken
        );
        if (!result.Succeeded)
        {
            throw new AboxException($"'sbx mcp ls' failed: {Describe(result)}");
        }

        return result
            .StandardOutput.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            )
            .ToList();
    }

    /// <summary>Starts the browser sign-in.</summary>
    public async Task<bool> SignInAsync(CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            new ProcessRequest(ExecutableName, ["login"], AttachConsole: true),
            cancellationToken
        );
        return result.Succeeded;
    }

    /// <summary>Registers an MCP server on the host.</summary>
    public async Task<int> AddMcpServerAsync(
        string name,
        string url,
        CancellationToken cancellationToken
    )
    {
        var result = await _runner.RunAsync(
            new ProcessRequest(ExecutableName, ["mcp", "add", name, "--url", url]),
            cancellationToken
        );
        return result.ExitCode;
    }

    private static string Describe(ProcessResult result)
    {
        var text = (result.StandardError + result.StandardOutput).Trim();
        return string.IsNullOrEmpty(text) ? $"exit code {result.ExitCode}" : text;
    }

    private static DiagnoseReport ParseDiagnostics(string json)
    {
        using var document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("checks", out var checks))
        {
            return new DiagnoseReport([]);
        }

        var parsed = new List<DiagnosticCheck>();
        foreach (var check in checks.EnumerateArray())
        {
            parsed.Add(
                new DiagnosticCheck(
                    String(check, "name"),
                    String(check, "status"),
                    String(check, "message"),
                    String(check, "hint")
                )
            );
        }

        return new DiagnoseReport(parsed);
    }

    private static IReadOnlyList<SandboxInfo> ParseSandboxes(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        var items =
            document.RootElement.ValueKind == JsonValueKind.Array
                ? document.RootElement.EnumerateArray().ToList()
                : [document.RootElement];

        var sandboxes = new List<SandboxInfo>();
        foreach (var item in items)
        {
            var name = String(item, "name");
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            sandboxes.Add(new SandboxInfo(name, String(item, "status"), ReadTimestamp(item)));
        }

        return sandboxes;
    }

    private static string String(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : value.ToString();
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement element)
    {
        foreach (var property in (string[])["createdAt", "created_at", "CreatedAt"])
        {
            if (
                element.TryGetProperty(property, out var value)
                && value.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(value.GetString(), out var timestamp)
            )
            {
                return timestamp;
            }
        }

        return null;
    }

    [GeneratedRegex(@"(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionPattern();
}
