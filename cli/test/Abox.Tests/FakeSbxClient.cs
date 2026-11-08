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

    public List<string> RegisteredMcpServers { get; } = [];

    public HashSet<string> McpServers { get; } = [];

    public int CreateExitCode { get; set; }

    public int ExecExitCode { get; set; }

    public Task<Version> GetVersionAsync(CancellationToken cancellationToken) => Task.FromResult(Version);

    public Task<DiagnoseReport> DiagnoseAsync(CancellationToken cancellationToken) => Task.FromResult(Report);

    public Task<IReadOnlyList<SandboxInfo>> ListSandboxesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<SandboxInfo>>(Sandboxes);

    public Task<SandboxInfo?> FindSandboxAsync(string name, CancellationToken cancellationToken) =>
        Task.FromResult(Sandboxes.FirstOrDefault(sandbox => sandbox.Name == name));

    public Task<int> CreateAsync(SbxCreateRequest request, CancellationToken cancellationToken)
    {
        Created.Add(request);
        return Task.FromResult(CreateExitCode);
    }

    public Task<int> RemoveSandboxAsync(string name, CancellationToken cancellationToken)
    {
        Removed.Add(name);
        Sandboxes.RemoveAll(sandbox => sandbox.Name == name);
        return Task.FromResult(0);
    }

    public Task<int> ExecAsync(SbxExecRequest request, CancellationToken cancellationToken)
    {
        Executed.Add(request);
        return Task.FromResult(ExecExitCode);
    }

    public Task<bool> HasProgramAsync(string sandbox, string program, CancellationToken cancellationToken) =>
        Task.FromResult(Programs.Contains(program));

    public Task<(int ExitCode, string Output)> InspectKitAsync(string kitDirectory, CancellationToken cancellationToken) =>
        Task.FromResult((0, string.Empty));

    public Task<IReadOnlyList<string>> ListMcpServersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>(McpServers.ToList());

    public Task<int> AddMcpServerAsync(string name, string url, CancellationToken cancellationToken)
    {
        RegisteredMcpServers.Add(name);
        McpServers.Add(name);
        return Task.FromResult(0);
    }

    public Task<bool> SignInAsync(CancellationToken cancellationToken) => Task.FromResult(true);
}
