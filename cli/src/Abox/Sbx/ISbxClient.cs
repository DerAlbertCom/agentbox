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

    Task<int> CreateAsync(SbxCreateRequest request, CancellationToken cancellationToken);

    Task<int> RemoveSandboxAsync(string name, CancellationToken cancellationToken);

    Task<int> ExecAsync(SbxExecRequest request, CancellationToken cancellationToken);

    Task<bool> HasProgramAsync(string sandbox, string program, CancellationToken cancellationToken);

    Task<(int ExitCode, string Output)> InspectKitAsync(string kitDirectory, CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> ListMcpServersAsync(CancellationToken cancellationToken);

    Task<int> AddMcpServerAsync(string name, string url, CancellationToken cancellationToken);

    Task<bool> SignInAsync(CancellationToken cancellationToken);
}
