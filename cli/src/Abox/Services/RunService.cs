using Abox.Infrastructure;
using Abox.Naming;
using Abox.Launchers;
using Abox.Sbx;

namespace Abox.Services;

/// <summary>What to run.</summary>
/// <param name="LauncherId">The launcher to start.</param>
/// <param name="Location">The folder whose sandbox is opened.</param>
/// <param name="Argv">The arguments after <c>--</c>.</param>
public sealed record RunRequest(string LauncherId, string Location, IReadOnlyList<string> Argv);

/// <summary>
/// Starts a launcher for the sandbox of a folder.
/// </summary>
public sealed class RunService
{
    private readonly LauncherRegistry _launchers;
    private readonly ISbxClient _sbx;
    private readonly IHostPrograms _hostPrograms;
    private readonly IConsole _console;

    public RunService(
        LauncherRegistry launchers,
        ISbxClient sbx,
        IHostPrograms hostPrograms,
        IConsole console)
    {
        _launchers = launchers;
        _sbx = sbx;
        _hostPrograms = hostPrograms;
        _console = console;
    }

    /// <summary>Starts the launcher and returns the exit code of the command.</summary>
    public async Task<int> RunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var launcher = _launchers.Get(request.LauncherId);

        foreach (var requirement in launcher.HostRequirements)
        {
            if (_hostPrograms.Exists(requirement.Program))
            {
                continue;
            }

            _console.WriteError($"The launcher '{launcher.Id}' needs '{requirement.Program}' on the host.");
            _console.WriteError(requirement.InstallHint);
            return 1;
        }

        var location = Path.GetFullPath(request.Location);
        var name = SandboxName.For(location);

        var sandbox = await _sbx.FindSandboxAsync(name, cancellationToken);
        if (sandbox is null)
        {
            _console.WriteError($"There is no sandbox for '{location}'.");
            _console.WriteError($"Create it with: abox create --location {location}");
            return 1;
        }

        foreach (var program in launcher.SandboxRequirements)
        {
            if (await _sbx.HasProgramAsync(name, program, cancellationToken))
            {
                continue;
            }

            _console.WriteError($"The image of '{name}' has no '{program}'. The image predates the '{launcher.Id}' launcher.");
            _console.WriteError($"Create the sandbox again for this folder: abox create --location {location}");
            return 1;
        }

        var context = new LauncherContext(
            location,
            name,
            ShellLauncher.WorkspacePath,
            request.Argv,
            _sbx,
            _console);

        return await launcher.RunAsync(context, cancellationToken);
    }
}
