namespace Abox.Launchers;

/// <summary>
/// The default launcher: a shell in the workspace of the sandbox.
/// </summary>
public sealed class ShellLauncher : ILauncher
{
    public string Id => "shell";

    public string Summary => "A shell in the workspace of the sandbox";

    public IReadOnlyList<HostRequirement> HostRequirements => [];

    public IReadOnlyList<string> SandboxRequirements => [];

    public Task<int> RunAsync(LauncherSession session, CancellationToken cancellationToken)
    {
        var command = session.Argv.Count > 0 ? session.Argv : new[] { "bash", "-l" };

        return session.ExecAsync(
            command,
            cancellationToken,
            allocateTty: true,
            keepStandardInputOpen: true
        );
    }
}
