using Abox.Configuration;
using Abox.Https;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Launchers;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sandboxes;
using Abox.Sbx;

namespace Abox.Features.Run;

/// <summary>What to run.</summary>
/// <param name="LauncherId">The launcher to start.</param>
/// <param name="Location">The folder whose sandbox is opened.</param>
/// <param name="Argv">The arguments after <c>--</c>.</param>
public sealed record RunRequest(string LauncherId, string Location, IReadOnlyList<string> Argv);

/// <summary>
/// Starts a launcher for the sandbox of a folder: the common Launcher steps in order, then
/// the launcher itself with a Launcher session.
/// </summary>
public sealed class RunFlow(
    LauncherRegistry launchers,
    SquadResolver resolver,
    BuildStore store,
    SandboxRecordStore records,
    SandboxCreator create,
    ISbxClient sbx,
    SandboxHttps https,
    FolderSandbox sandboxes,
    CreateRecoveryHints recoveryHints,
    IHostPrograms hostPrograms,
    IConsole console
)
{
    /// <summary>Starts the launcher and returns the exit code of the command.</summary>
    public async Task<int> RunAsync(RunRequest request, CancellationToken cancellationToken)
    {
        var launcher = launchers.Get(request.LauncherId);

        foreach (var requirement in launcher.HostRequirements)
        {
            if (hostPrograms.Exists(requirement.Program))
            {
                continue;
            }

            console.WriteError(
                $"The launcher '{launcher.Id}' needs '{requirement.Program}' on the host."
            );
            console.WriteError(requirement.InstallHint);
            return 1;
        }

        var location = Path.GetFullPath(request.Location);
        var folderConfig = await FolderConfig.LoadAsync(location, cancellationToken);

        SandboxInfo sandbox;
        try
        {
            sandbox = await sandboxes.RequireAsync(
                location,
                folderConfig,
                note: null,
                cancellationToken
            );
        }
        catch (MissingFolderSandboxException exception)
        {
            console.WriteError(exception.Message);
            return 1;
        }

        if (!await ReplaceWithANewerBuildAsync(sandbox, location, folderConfig, cancellationToken))
        {
            return 1;
        }

        foreach (var program in launcher.SandboxRequirements)
        {
            if (await sbx.HasProgramAsync(sandbox.Name, program, cancellationToken))
            {
                continue;
            }

            console.WriteError(
                $"The image of '{sandbox.Name}' has no '{program}'. The image predates the '{launcher.Id}' launcher."
            );
            WriteCreateHint(
                "Create the sandbox again for this folder: ",
                sandbox.Name,
                location,
                folderConfig
            );
            return 1;
        }

        var session = new LauncherSession(
            location,
            sandbox.Name,
            await WorkspaceOfAsync(sandbox.Name, location, cancellationToken),
            request.Argv,
            sbx,
            https
        );

        return await launcher.RunAsync(session, cancellationToken);
    }

    private void WriteCreateHint(
        string prefix,
        string name,
        string location,
        FolderConfig? folderConfig
    )
    {
        foreach (var line in recoveryHints.Resolve(name, location, folderConfig).Lines(prefix))
            console.WriteError(line);
    }

    /// <summary>
    /// Offers to replace the sandbox when the build of its squad is newer than the one it
    /// was made of. Returns false when the command must not go on.
    /// </summary>
    private async Task<bool> ReplaceWithANewerBuildAsync(
        SandboxInfo sandbox,
        string location,
        FolderConfig? folderConfig,
        CancellationToken cancellationToken
    )
    {
        var record = records.Load(sandbox.Name);
        if (record is null || record.KitReference is not null)
        {
            return true;
        }

        // A record without a team comes from an older version of abox; the command then
        // cannot say which build is newer.
        if (string.IsNullOrEmpty(record.Team))
        {
            return true;
        }

        BuildManifest? manifest;
        try
        {
            manifest = store.Load(new SquadBuildIdentity(record.Team, record.Squad, record.Tag));
        }
        catch (AboxException exception)
        {
            // Invalid or unrelated local metadata must not interrupt an existing sandbox.
            console.WriteError(exception.Message);
            return true;
        }
        if (manifest?.LocalWorkload is null || manifest.CreatedAt <= record.BuildCreatedAt)
        {
            return true;
        }

        console.WriteLine(
            $"The squad '{record.Squad}' has a newer build, from {manifest.CreatedAt:yyyy-MM-dd HH:mm}."
        );

        if (!console.Confirm("Replace the sandbox with it?"))
        {
            return true;
        }

        folderConfig?.EnsureSandboxSquad(record.Squad);
        var team = resolver.ResolveTeam(
            string.IsNullOrWhiteSpace(manifest.RootDirectory) ? location : manifest.RootDirectory,
            record.Team
        );

        return await create.CreateAsync(
                new SandboxCreationRequest(
                    team,
                    record.Squad,
                    location,
                    record.Tag,
                    folderConfig,
                    AssumeYes: true,
                    Local: true,
                    ExistingSandbox: sandbox
                ),
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
        var workspace = await sbx.GetWorkspaceDirectoryAsync(name, cancellationToken);
        return workspace ?? location;
    }
}
