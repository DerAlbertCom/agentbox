using Abox.Kits;

namespace Abox.Sbx;

/// <summary>
/// What abox asks of sbx. The interface keeps the callers testable and the wrapper
/// class the only place that knows the flags.
/// </summary>
public interface ISbxClient
{
    Task<string> GetDevelopmentCertificatePasswordAsync(
        string sandbox,
        CancellationToken cancellationToken
    );
    Task ActivateDevelopmentCertificateAsync(
        string sandbox,
        ReadOnlyMemory<byte> pfx,
        string password,
        CancellationToken cancellationToken
    );
    Task VerifyDevelopmentCertificateActivationAsync(
        string sandbox,
        CancellationToken cancellationToken
    );

    /// <summary>Refreshes only the verified reader of an existing protected HTTPS runtime.</summary>
    Task RefreshDevelopmentCertificateBootstrapAsync(
        string sandbox,
        CancellationToken cancellationToken
    );

    /// <summary>The first sbx version abox works with.</summary>
    const string MinimumVersion = "0.51.0";

    Task<Version> GetVersionAsync(CancellationToken cancellationToken);

    Task<DiagnoseReport> DiagnoseAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SandboxInfo>> ListSandboxesAsync(CancellationToken cancellationToken);

    Task<SandboxInfo?> FindSandboxAsync(string name, CancellationToken cancellationToken);

    /// <summary>Starts the sandbox if needed and ensures loopback TCP app mappings without removing others.</summary>
    Task EnsureApplicationPortsAsync(
        string sandbox,
        IReadOnlyList<ApplicationPortMapping> mappings,
        CancellationToken cancellationToken
    );

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

    /// <summary>
    /// Runs a command in the sandbox. Every exec carries the protected HTTPS bootstrap: when
    /// the Sandbox has a protected HTTPS runtime, the verified reader sets the Kestrel
    /// certificate and Node extra-CA environment first; a missing, changed or refusing
    /// reader fails closed with its exit code (78 when unverifiable) and the command does
    /// not run. Without a runtime the command runs unchanged.
    /// </summary>
    Task<int> ExecAsync(SbxExecRequest request, CancellationToken cancellationToken);

    Task<bool> HasProgramAsync(string sandbox, string program, CancellationToken cancellationToken);

    /// <summary>Where the runtime put the workspace of the sandbox, when it says.</summary>
    Task<string?> GetWorkspaceDirectoryAsync(string sandbox, CancellationToken cancellationToken);

    /// <summary>Checks effective application-user paths for all stores without creating files.</summary>
    Task ValidateUserSecretsDestinationAsync(
        string sandbox,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken
    );

    /// <summary>Explicit sandbox-declared TCP host ports currently authorized for this sandbox.</summary>
    Task<IReadOnlyList<int>> GetUserSecretsHostPortsAsync(
        string sandbox,
        CancellationToken cancellationToken
    );

    /// <summary>Replaces one store atomically; payload and frame travel only over sensitive UTF-8 stdin.</summary>
    Task WriteUserSecretsAsync(
        string sandbox,
        string id,
        string json,
        CancellationToken cancellationToken
    );

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
        string? host,
        string? environmentVariable,
        string value,
        CancellationToken cancellationToken
    );

    Task<SbxOutcome> RemoveSecretAsync(
        SecretDeclaration declaration,
        CancellationToken cancellationToken
    );

    Task<bool> SignInAsync(CancellationToken cancellationToken);
}
