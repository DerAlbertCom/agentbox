using Abox.Build;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Launchers;
using Abox.Naming;
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
    private readonly BuildStore _store;
    private readonly SandboxRecordStore _records;
    private readonly CreateService _create;
    private readonly ISbxClient _sbx;
    private readonly IHostPrograms _hostPrograms;
    private readonly IConsole _console;

    public RunService(
        LauncherRegistry launchers,
        BuildStore store,
        SandboxRecordStore records,
        CreateService create,
        ISbxClient sbx,
        IHostPrograms hostPrograms,
        IConsole console
    )
    {
        _launchers = launchers;
        _store = store;
        _records = records;
        _create = create;
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

            _console.WriteError(
                $"The launcher '{launcher.Id}' needs '{requirement.Program}' on the host."
            );
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

        if (!await ReplaceWithANewerBuildAsync(name, location, cancellationToken))
        {
            return 1;
        }

        foreach (var program in launcher.SandboxRequirements)
        {
            if (await _sbx.HasProgramAsync(name, program, cancellationToken))
            {
                continue;
            }

            _console.WriteError(
                $"The image of '{name}' has no '{program}'. The image predates the '{launcher.Id}' launcher."
            );
            _console.WriteError(
                $"Create the sandbox again for this folder: abox create --location {location}"
            );
            return 1;
        }

        var context = new LauncherContext(
            location,
            name,
            await WorkspaceOfAsync(name, location, cancellationToken),
            request.Argv,
            _sbx,
            _console
        );

        return await launcher.RunAsync(context, cancellationToken);
    }

    /// <summary>
    /// Offers to replace the sandbox when the build of its squad is newer than the one it
    /// was made of. Returns false when the command must not go on.
    /// </summary>
    private async Task<bool> ReplaceWithANewerBuildAsync(
        string name,
        string location,
        CancellationToken cancellationToken
    )
    {
        var record = _records.Load(name);
        if (record is null)
        {
            return true;
        }

        var manifest = _store.Load(record.Squad, record.Tag);
        if (manifest is null || manifest.CreatedAt <= record.BuildCreatedAt)
        {
            return true;
        }

        _console.WriteLine(
            $"The squad '{record.Squad}' has a newer build, from {manifest.CreatedAt:yyyy-MM-dd HH:mm}."
        );

        if (!_console.Confirm("Replace the sandbox with it?"))
        {
            return true;
        }

        return await _create.CreateAsync(
                new CreateRequest(record.Squad, location, record.Tag, AssumeYes: true),
                cancellationToken
            ) == 0;
    }

    /// <summary>
    /// Where the workspace of the sandbox is. The runtime says so; when it does not, the
    /// folder of the host is the best guess.
    /// </summary>
    private async Task<string> WorkspaceOfAsync(
        string name,
        string location,
        CancellationToken cancellationToken
    )
    {
        var workspace = await _sbx.GetWorkspaceDirectoryAsync(name, cancellationToken);
        return workspace ?? location;
    }
}
