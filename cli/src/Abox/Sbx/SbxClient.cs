using System.Text.Json;
using System.Text.RegularExpressions;
using Abox.Infrastructure;

namespace Abox.Sbx;

/// <summary>
/// Every call abox makes to <c>sbx</c>. Keeping them in one place keeps the flags and the
/// version check in one place too.
/// </summary>
public sealed partial class SbxClient(IProcessRunner runner) : ISbxClient
{
    private const string ExecutableName = "sbx";

    /// <summary>The version of the installed sbx client.</summary>
    public async Task<Version> GetVersionAsync(CancellationToken cancellationToken)
    {
        // The JSON format is read the same way as every other call, so the version
        // does not depend on the words of the plain text.
        var report = await RunJsonAsync<SbxVersion>(
            ["version", "--json"],
            "sbx version",
            cancellationToken
        );
        return ParseVersion(report.Client?.Version ?? string.Empty);
    }

    /// <summary>The report of <c>sbx diagnose</c>.</summary>
    public async Task<DiagnoseReport> DiagnoseAsync(CancellationToken cancellationToken)
    {
        var report = await RunJsonAsync<SbxDiagnose>(
            ["diagnose", "--json"],
            "sbx diagnose",
            cancellationToken
        );
        return new DiagnoseReport(
            report
                .Checks.Select(check => new DiagnosticCheck(
                    check.Name ?? string.Empty,
                    check.Status ?? string.Empty,
                    check.Message ?? string.Empty,
                    check.Hint ?? string.Empty
                ))
                .ToList()
        );
    }

    /// <summary>The sandboxes sbx knows about.</summary>
    public async Task<IReadOnlyList<SandboxInfo>> ListSandboxesAsync(
        CancellationToken cancellationToken
    )
    {
        var sandboxes = await RunJsonAsync<List<SbxSandbox>>(
            ["ls", "--json"],
            "sbx ls",
            cancellationToken
        );
        return sandboxes
            .Where(sandbox => !string.IsNullOrEmpty(sandbox.Name))
            .Select(sandbox => new SandboxInfo(
                sandbox.Name!,
                sandbox.Status ?? string.Empty,
                sandbox.CreatedAt
            ))
            .ToList();
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

    /// <summary>The template images the local runtime holds.</summary>
    public async Task<IReadOnlyList<TemplateImage>> ListTemplatesAsync(
        CancellationToken cancellationToken
    )
    {
        var templates = await RunJsonAsync<List<SbxTemplate>>(
            ["template", "ls", "--json"],
            "sbx template ls",
            cancellationToken
        );
        return templates
            .Where(template => !string.IsNullOrEmpty(template.Tag))
            .Select(template => new TemplateImage(
                template.Repository ?? string.Empty,
                template.Tag!,
                template.Id ?? string.Empty,
                template.CreatedAt
            ))
            .ToList();
    }

    /// <summary>Removes one template image by reference, without asking.</summary>
    public Task<SbxOutcome> RemoveTemplateAsync(
        string reference,
        CancellationToken cancellationToken
    )
    {
        return RunOutcomeAsync(
            new ProcessRequest(ExecutableName, ["template", "rm", reference, "--force"]),
            cancellationToken
        );
    }

    /// <summary>Creates a sandbox from a workload kit and its mixins.</summary>
    public Task<SbxOutcome> CreateAsync(
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
            "--pull",
            request.PullPolicy,
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

        return RunOutcomeAsync(
            new ProcessRequest(
                ExecutableName,
                arguments,
                Environment: request.HostDockerContext is { } context
                    ? new Dictionary<string, string>
                    {
                        ["SBX_KIT_BUILDER"] = "host",
                        ["BUILDX_BUILDER"] = context,
                        ["DOCKER_CONTEXT"] = context,
                    }
                    : null
            ),
            cancellationToken
        );
    }

    /// <summary>Removes a sandbox without asking.</summary>
    public Task<SbxOutcome> RemoveSandboxAsync(string name, CancellationToken cancellationToken)
    {
        return RunOutcomeAsync(
            new ProcessRequest(ExecutableName, ["rm", name, "--force"]),
            cancellationToken
        );
    }

    /// <summary>Stops one sandbox without removing it.</summary>
    public Task<SbxOutcome> StopSandboxAsync(string name, CancellationToken cancellationToken)
    {
        return RunOutcomeAsync(
            new ProcessRequest(ExecutableName, ["stop", name]),
            cancellationToken
        );
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

        var result = await runner.RunAsync(
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
        var result = await runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                ["exec", sandbox, "sh", "-c", "command -v \"$1\"", "_", program]
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
        var result = await runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                ["exec", sandbox, "sh", "-c", "printf %s \"$WORKSPACE_DIR\""]
            ),
            cancellationToken
        );

        var directory = result.StandardOutput.Trim();
        return string.IsNullOrEmpty(directory) ? null : directory;
    }

    /// <summary>Reads kit declarations; this command does not build recipe-based kits.</summary>
    public Task<SbxOutcome> InspectKitAsync(
        string kitDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? arguments = null
    )
    {
        var command = new List<string> { "kit", "inspect", kitDirectory };
        foreach (var (name, value) in arguments ?? new Dictionary<string, string>())
        {
            command.AddRange(["--kit-arg", $"{name}={value}"]);
        }
        return RunOutcomeAsync(new ProcessRequest(ExecutableName, command), cancellationToken);
    }

    /// <summary>The MCP servers registered on the host.</summary>
    public async Task<IReadOnlyList<string>> ListMcpServersAsync(
        CancellationToken cancellationToken
    )
    {
        var servers = await RunJsonAsync<List<SbxMcpServer>>(
            ["mcp", "ls", "--json"],
            "sbx mcp ls",
            cancellationToken
        );
        return servers
            .Select(server => server.Name)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList();
    }

    /// <summary>The secrets stored on the host.</summary>
    public async Task<HostSecrets> ListSecretsAsync(CancellationToken cancellationToken)
    {
        var list = await RunJsonAsync<SbxSecrets>(
            ["secret", "ls", "--json"],
            "sbx secret ls",
            cancellationToken
        );
        return new HostSecrets(
            list.Secrets.Select(secret => secret.Name)
                .Where(name => !string.IsNullOrEmpty(name))
                .Select(name => name!)
                .ToList(),
            list.CustomSecrets.Select(secret => new CustomSecret(
                    secret.Targets.Where(target => !string.IsNullOrEmpty(target)).ToList(),
                    secret.EnvironmentVariable
                ))
                .ToList()
        );
    }

    /// <summary>
    /// Stores the secret of a service sbx knows. The value goes through the standard
    /// input, so it stays out of the process list.
    /// </summary>
    public Task<SbxOutcome> SetSecretAsync(
        string service,
        string value,
        CancellationToken cancellationToken
    )
    {
        return RunOutcomeAsync(
            new ProcessRequest(ExecutableName, ["secret", "set", service], StandardInput: value),
            cancellationToken
        );
    }

    /// <summary>
    /// Stores the secret of a service sbx does not know. The value goes through the
    /// standard input, so it stays out of the process list.
    /// </summary>
    public Task<SbxOutcome> SetCustomSecretAsync(
        string service,
        string? host,
        string? environmentVariable,
        string value,
        CancellationToken cancellationToken
    )
    {
        var arguments = new List<string> { "secret", "set-custom" };

        if (host is not null)
        {
            arguments.Add("--host");
            arguments.Add(host);
        }

        if (environmentVariable is not null)
        {
            arguments.Add("--env");
            arguments.Add(environmentVariable);
        }

        arguments.Add("--value-stdin");

        return RunOutcomeAsync(
            new ProcessRequest(ExecutableName, arguments, StandardInput: value),
            cancellationToken
        );
    }

    /// <summary>
    /// Removes a secret from the host store. A secret of a service sbx does not know is
    /// removed by its target host, or by its environment variable when it has no host.
    /// </summary>
    public Task<SbxOutcome> RemoveSecretAsync(
        string service,
        string? host,
        CancellationToken cancellationToken,
        string? environmentVariable = null
    )
    {
        var arguments = new List<string> { "secret", "rm" };

        if (host is not null)
        {
            arguments.Add("--host");
            arguments.Add(host);
        }
        else if (environmentVariable is not null)
        {
            arguments.Add("--env-name");
            arguments.Add(environmentVariable);
        }
        else
        {
            arguments.Add(service);
        }

        arguments.Add("--force");

        return RunOutcomeAsync(new ProcessRequest(ExecutableName, arguments), cancellationToken);
    }

    /// <summary>Starts the browser sign-in.</summary>
    public async Task<bool> SignInAsync(CancellationToken cancellationToken)
    {
        var result = await runner.RunAsync(
            new ProcessRequest(ExecutableName, ["login"], AttachConsole: true),
            cancellationToken
        );
        return result.Succeeded;
    }

    /// <summary>Registers an MCP server on the host.</summary>
    public Task<SbxOutcome> AddMcpServerAsync(
        string name,
        string url,
        CancellationToken cancellationToken
    )
    {
        return RunOutcomeAsync(
            new ProcessRequest(ExecutableName, ["mcp", "add", name, "--url", url]),
            cancellationToken
        );
    }

    private async Task<SbxOutcome> RunOutcomeAsync(
        ProcessRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await runner.RunAsync(request, cancellationToken);
        return new SbxOutcome(result.ExitCode, result.StandardOutput + result.StandardError);
    }

    private async Task<ProcessResult> RunOrThrowAsync(
        ProcessRequest request,
        string command,
        CancellationToken cancellationToken
    )
    {
        var result = await runner.RunAsync(request, cancellationToken);
        if (!result.Succeeded)
        {
            throw new AboxException($"'{command}' failed: {Describe(result)}");
        }

        return result;
    }

    private async Task<T> RunJsonAsync<T>(
        IReadOnlyList<string> arguments,
        string command,
        CancellationToken cancellationToken
    )
        where T : class
    {
        var result = await RunOrThrowAsync(
            new ProcessRequest(ExecutableName, arguments),
            command,
            cancellationToken
        );
        return Deserialize<T>(result.StandardOutput, command);
    }

    /// <summary>The JSON sbx wrote, as a typed result.</summary>
    private static T Deserialize<T>(string json, string command)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new AboxException($"'{command}' wrote no output.");
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json)
                ?? throw new AboxException($"'{command}' wrote no output.");
        }
        catch (JsonException exception)
        {
            throw new AboxException(
                $"Cannot read the output of '{command}': {exception.Message}",
                exception
            );
        }
    }

    private static string Describe(ProcessResult result)
    {
        var text = (result.StandardError + result.StandardOutput).Trim();
        return string.IsNullOrEmpty(text) ? $"exit code {result.ExitCode}" : text;
    }

    /// <summary>Reads the sbx version, like <c>v0.51.0</c> or <c>0.51.0</c>.</summary>
    private static Version ParseVersion(string text)
    {
        var match = VersionPattern().Match(text);
        if (!match.Success)
        {
            throw new AboxException($"Cannot read the sbx version from '{text}'.");
        }

        return new Version(
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value)
        );
    }

    [GeneratedRegex(@"(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex VersionPattern();
}
