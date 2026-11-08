using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Launchers;

/// <summary>
/// A program the host needs before a launcher can start, and how to get it.
/// </summary>
/// <param name="Program">The program, as it is called on the host.</param>
/// <param name="InstallHint">What to do when the program is missing.</param>
public sealed record HostRequirement(string Program, string InstallHint);

/// <summary>
/// What a launcher gets when it starts.
/// </summary>
/// <param name="Location">The folder the sandbox works on.</param>
/// <param name="SandboxName">The name of the sandbox.</param>
/// <param name="WorkspacePath">Where the workspace is inside the sandbox.</param>
/// <param name="Argv">The arguments after <c>--</c>.</param>
/// <param name="Sbx">The sbx wrapper.</param>
/// <param name="Console">The terminal.</param>
public sealed record LauncherContext(
    string Location,
    string SandboxName,
    string WorkspacePath,
    IReadOnlyList<string> Argv,
    ISbxClient Sbx,
    IConsole Console);

/// <summary>
/// A way to open something for the user in a sandbox.
/// </summary>
public interface ILauncher
{
    /// <summary>The unique lowercase id of the launcher.</summary>
    string Id { get; }

    /// <summary>The one-line summary shown in <c>abox run --help</c>.</summary>
    string Summary { get; }

    /// <summary>The programs the host needs.</summary>
    IReadOnlyList<HostRequirement> HostRequirements { get; }

    /// <summary>The programs the sandbox needs.</summary>
    IReadOnlyList<string> SandboxRequirements { get; }

    /// <summary>Starts the launcher and returns its exit code.</summary>
    Task<int> RunAsync(LauncherContext context, CancellationToken cancellationToken);
}
