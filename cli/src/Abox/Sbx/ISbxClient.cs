namespace Abox.Sbx;

/// <summary>
/// What abox asks of sbx. The interface keeps the callers testable and the wrapper
/// class the only place that knows the flags.
/// </summary>
public interface ISbxClient
{
    /// <summary>The first sbx version abox works with.</summary>
    const string MinimumVersion = "0.51.0";

    Task<Version> GetVersionAsync(CancellationToken cancellationToken);

    Task<DiagnoseReport> DiagnoseAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SandboxInfo>> ListSandboxesAsync(CancellationToken cancellationToken);

    Task<SandboxInfo?> FindSandboxAsync(string name, CancellationToken cancellationToken);

    /// <summary>The template images the local runtime holds.</summary>
    Task<IReadOnlyList<TemplateImage>> ListTemplatesAsync(CancellationToken cancellationToken);

    /// <summary>Removes one template image by reference, without asking.</summary>
    Task<SbxOutcome> RemoveTemplateAsync(string reference, CancellationToken cancellationToken);

    Task<SbxOutcome> CreateAsync(SbxCreateRequest request, CancellationToken cancellationToken);

    /// <summary>Adds a validated repository prefix to kit.allowedSources if it is not already covered.</summary>
    Task EnsureKitSourceAllowedAsync(string repositoryPrefix, CancellationToken cancellationToken);

    Task<SbxOutcome> RemoveSandboxAsync(string name, CancellationToken cancellationToken);

    /// <summary>Stops one sandbox without removing it.</summary>
    Task<SbxOutcome> StopSandboxAsync(string name, CancellationToken cancellationToken);

    Task<int> ExecAsync(SbxExecRequest request, CancellationToken cancellationToken);

    Task<bool> HasProgramAsync(string sandbox, string program, CancellationToken cancellationToken);

    /// <summary>Where the runtime put the workspace of the sandbox, when it says.</summary>
    Task<string?> GetWorkspaceDirectoryAsync(string sandbox, CancellationToken cancellationToken);

    Task<SbxOutcome> InspectKitAsync(
        string kitDirectory,
        CancellationToken cancellationToken,
        IReadOnlyDictionary<string, string>? arguments = null
    );

    Task<IReadOnlyList<string>> ListMcpServersAsync(CancellationToken cancellationToken);

    Task<SbxOutcome> AddMcpServerAsync(
        string name,
        string url,
        CancellationToken cancellationToken
    );

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
        string? environmentVariable,
        CancellationToken cancellationToken
    );

    Task<bool> SignInAsync(CancellationToken cancellationToken);
}
