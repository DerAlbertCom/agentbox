using Abox.Sbx;

namespace Abox.Sbx;

/// <summary>
/// What abox asks of sbx. The interface keeps the callers testable and the wrapper
/// class the only place that knows the flags.
/// </summary>
public interface ISbxClient
{
    Task<Version> GetVersionAsync(CancellationToken cancellationToken);

    Task<DiagnoseReport> DiagnoseAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SandboxInfo>> ListSandboxesAsync(CancellationToken cancellationToken);

    Task<SandboxInfo?> FindSandboxAsync(string name, CancellationToken cancellationToken);

    Task<SbxOutcome> CreateAsync(SbxCreateRequest request, CancellationToken cancellationToken);
    Task<int> RemoveSandboxAsync(string name, CancellationToken cancellationToken);

    Task<int> ExecAsync(SbxExecRequest request, CancellationToken cancellationToken);

    Task<bool> HasProgramAsync(string sandbox, string program, CancellationToken cancellationToken);

    /// <summary>Where the runtime put the workspace of the sandbox, when it says.</summary>
    Task<string?> GetWorkspaceDirectoryAsync(string sandbox, CancellationToken cancellationToken);

    Task<(int ExitCode, string Output)> InspectKitAsync(
        string kitDirectory,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<string>> ListMcpServersAsync(CancellationToken cancellationToken);

    Task<int> AddMcpServerAsync(string name, string url, CancellationToken cancellationToken);

    Task<HostSecrets> ListSecretsAsync(CancellationToken cancellationToken);

    Task<SbxOutcome> SetSecretAsync(
        string service,
        string value,
        CancellationToken cancellationToken
    );

    Task<SbxOutcome> SetCustomSecretAsync(
        string service,
        string? host,
        string? environmentVariable,
        string value,
        CancellationToken cancellationToken
    );

    Task<SbxOutcome> RemoveSecretAsync(
        string service,
        string? host,
        CancellationToken cancellationToken
    );

    Task<bool> SignInAsync(CancellationToken cancellationToken);
}
