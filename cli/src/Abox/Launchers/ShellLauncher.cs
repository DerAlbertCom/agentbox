using Abox.Sbx;

namespace Abox.Launchers;

/// <summary>
/// The default launcher: a shell in the workspace of the sandbox.
/// </summary>
public sealed class ShellLauncher : ILauncher
{
    /// <summary>Where the workspace is inside the sandbox.</summary>
    public const string WorkspacePath = "/home/agent/workspace";

    public string Id => "shell";

    public string Summary => "A shell in the workspace of the sandbox";

    public IReadOnlyList<HostRequirement> HostRequirements => [];

    public IReadOnlyList<string> SandboxRequirements => [];

    public async Task<int> RunAsync(LauncherContext context, CancellationToken cancellationToken)
    {
        var command = context.Argv.Count > 0 ? context.Argv : new[] { "bash", "-l" };

        return await context.Sbx.ExecAsync(
            new SbxExecRequest(
                context.SandboxName,
                command,
                AllocateTty: true,
                KeepStandardInputOpen: true,
                WorkingDirectory: WorkspacePath),
            cancellationToken);
    }
}
