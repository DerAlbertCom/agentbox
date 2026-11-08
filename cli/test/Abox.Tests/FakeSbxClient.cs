using Abox.Sbx;

namespace Abox.Tests;

/// <summary>
/// An sbx that answers what the test tells it to.
/// </summary>
public sealed class FakeSbxClient : ISbxClient
{
    public Version Version { get; set; } = new(0, 51, 0);

    public DiagnoseReport Report { get; set; } = new([]);

    public List<SandboxInfo> Sandboxes { get; } = [];

    public List<SbxCreateRequest> Created { get; } = [];

    public List<string> Removed { get; } = [];

    public List<SbxExecRequest> Executed { get; } = [];

    public List<string> Programs { get; } = [];

    /// <summary>Where the runtime put the workspace, when the test says so.</summary>
    public string? WorkspaceDirectory { get; set; }

    public List<string> RegisteredMcpServers { get; } = [];

    public HashSet<string> McpServers { get; } = [];

    /// <summary>The secrets that are stored on the host.</summary>
    public HashSet<string> Secrets { get; } = [];

    /// <summary>The hosts of the custom secrets that are stored.</summary>
    public List<string> CustomTargets { get; } = [];

    /// <summary>The secrets that were set, with their values.</summary>
    public List<(string Service, string Value)> SetSecrets { get; } = [];

    /// <summary>The custom secrets that were set.</summary>
    public List<(
        string Service,
        string? Host,
        string? EnvironmentVariable,
        string Value
    )> CustomSecrets { get; } = [];

    /// <summary>The secrets that were removed.</summary>
    public List<string> RemovedSecrets { get; } = [];

    public int CreateExitCode { get; set; }

    public int ExecExitCode { get; set; }

    public Task<Version> GetVersionAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Version);

    public Task<DiagnoseReport> DiagnoseAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Report);

    public Task<IReadOnlyList<SandboxInfo>> ListSandboxesAsync(
        CancellationToken cancellationToken
    ) => Task.FromResult<IReadOnlyList<SandboxInfo>>(Sandboxes);

    public Task<SandboxInfo?> FindSandboxAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(Sandboxes.FirstOrDefault(sandbox => sandbox.Name == name));

    public Task<SbxOutcome> CreateAsync(
        SbxCreateRequest request,
        CancellationToken cancellationToken
    )
    {
        Created.Add(request);
        return Task.FromResult(new SbxOutcome(CreateExitCode, string.Empty));
    }

    public Task<SbxOutcome> RemoveSandboxAsync(string name, CancellationToken cancellationToken)
    {
        Removed.Add(name);
        Sandboxes.RemoveAll(sandbox => sandbox.Name == name);
        return Task.FromResult(new SbxOutcome(0, string.Empty));
    }

    public Task<int> ExecAsync(SbxExecRequest request, CancellationToken cancellationToken)
    {
        Executed.Add(request);
        return Task.FromResult(ExecExitCode);
    }

    public Task<bool> HasProgramAsync(
        string sandbox,
        string program,
        CancellationToken cancellationToken
    ) => Task.FromResult(Programs.Contains(program));

    public Task<string?> GetWorkspaceDirectoryAsync(
        string sandbox,
        CancellationToken cancellationToken
    ) => Task.FromResult(WorkspaceDirectory);

    public Task<SbxOutcome> InspectKitAsync(
        string kitDirectory,
        CancellationToken cancellationToken
    ) => Task.FromResult(new SbxOutcome(0, string.Empty));

    public Task<IReadOnlyList<string>> ListMcpServersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(McpServers.ToList());

    public Task<SbxOutcome> AddMcpServerAsync(
        string name,
        string url,
        CancellationToken cancellationToken
    )
    {
        RegisteredMcpServers.Add(name);
        McpServers.Add(name);
        return Task.FromResult(new SbxOutcome(0, string.Empty));
    }

    public Task<bool> SignInAsync(CancellationToken cancellationToken) => Task.FromResult(true);

    public Task<HostSecrets> ListSecretsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(
            new HostSecrets(
                Secrets.ToList(),
                CustomTargets.Select(target => new CustomSecret([target], null)).ToList()
            )
        );

    public Task<SbxOutcome> SetSecretAsync(
        string service,
        string value,
        CancellationToken cancellationToken
    )
    {
        SetSecrets.Add((service, value));
        Secrets.Add(service);
        return Task.FromResult(new SbxOutcome(0, string.Empty));
    }

    public Task<SbxOutcome> SetCustomSecretAsync(
        string service,
        string? host,
        string? environmentVariable,
        string value,
        CancellationToken cancellationToken
    )
    {
        SetSecrets.Add((service, value));
        CustomSecrets.Add((service, host, environmentVariable, value));
        if (host is not null)
        {
            CustomTargets.Add(host);
        }
        return Task.FromResult(new SbxOutcome(0, string.Empty));
    }

    public Task<SbxOutcome> RemoveSecretAsync(
        string service,
        string? host,
        CancellationToken cancellationToken
    )
    {
        RemovedSecrets.Add(service);
        Secrets.Remove(service);
        if (host is not null)
        {
            CustomTargets.Remove(host);
        }

        return Task.FromResult(new SbxOutcome(0, string.Empty));
    }
}
