using Abox.Https;
using Abox.Sbx;

namespace Abox.Launchers;

/// <summary>
/// What a launcher gets when it starts: where it runs and the only ways it reaches the
/// sandbox.
/// </summary>
public sealed class LauncherSession(
    string location,
    string sandboxName,
    string workspacePath,
    IReadOnlyList<string> argv,
    ISbxClient sbx,
    SandboxHttps https
)
{
    /// <summary>The folder the sandbox works on.</summary>
    public string Location => location;

    /// <summary>The name of the sandbox.</summary>
    public string SandboxName => sandboxName;

    /// <summary>Where the workspace is inside the sandbox.</summary>
    public string WorkspacePath => workspacePath;

    /// <summary>The arguments after <c>--</c>.</summary>
    public IReadOnlyList<string> Argv => argv;

    /// <summary>Runs a command in the sandbox and returns its exit code.</summary>
    public Task<int> ExecAsync(
        IReadOnlyList<string> command,
        CancellationToken cancellationToken,
        bool allocateTty = false,
        bool keepStandardInputOpen = false,
        string? workingDirectory = null
    ) =>
        sbx.ExecAsync(
            new SbxExecRequest(
                sandboxName,
                command,
                allocateTty,
                keepStandardInputOpen,
                workingDirectory
            ),
            cancellationToken
        );

    /// <summary>Fails before anything starts when protected HTTPS refuses the launch.</summary>
    public Task EnsureLaunchReadyAsync(CancellationToken cancellationToken) =>
        https.EnsureLaunchReadyAsync(sandboxName, cancellationToken);
}
