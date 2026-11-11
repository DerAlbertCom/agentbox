using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Launchers;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sbx;

namespace Abox.Features.Run;

/// <summary>What to run.</summary>
/// <param name="LauncherId">The launcher to start.</param>
/// <param name="Location">The folder whose sandbox is opened.</param>
/// <param name="Argv">The arguments after <c>--</c>.</param>
public sealed record RunRequest(string LauncherId, string Location, IReadOnlyList<string> Argv);

/// <summary>
/// Starts a launcher for the sandbox of a folder.
/// </summary>
public sealed class RunService(
    LauncherRegistry launchers,
    AboxConfig config,
    SquadResolver resolver,
    BuildStore store,
    SandboxRecordStore records,
    SandboxCreator create,
    ISbxClient sbx,
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
        var name = SandboxName.For(location);

        var sandbox = await sbx.FindSandboxAsync(name, cancellationToken);
        if (sandbox is null)
        {
            console.WriteError($"There is no sandbox for '{location}'.");
            console.WriteError($"Create it with: {CreateCommandFor(name, location)}");
            return 1;
        }

        if (!await ReplaceWithANewerBuildAsync(name, location, cancellationToken))
        {
            return 1;
        }

        foreach (var program in launcher.SandboxRequirements)
        {
            if (await sbx.HasProgramAsync(name, program, cancellationToken))
            {
                continue;
            }

            console.WriteError(
                $"The image of '{name}' has no '{program}'. The image predates the '{launcher.Id}' launcher."
            );
            console.WriteError(
                $"Create the sandbox again for this folder: {CreateCommandFor(name, location)}"
            );
            return 1;
        }

        var context = new LauncherContext(
            location,
            name,
            await WorkspaceOfAsync(name, location, cancellationToken),
            request.Argv,
            sbx,
            console
        );

        return await launcher.RunAsync(context, cancellationToken);
    }

    private string CreateCommandFor(string name, string location)
    {
        var record = records.Load(name);
        var squad = string.IsNullOrWhiteSpace(record?.Squad) ? null : record.Squad;
        var tag = string.IsNullOrWhiteSpace(record?.Tag) ? config.DefaultTag : record.Tag;
        var manifest =
            record is { KitReference: null } && squad is not null ? store.Load(squad, tag) : null;
        var team = string.IsNullOrWhiteSpace(record?.Team) ? null : record.Team;
        if (team is null && record is not null)
        {
            var reference = manifest?.Reference;
            var prefix = $"agentbox-{ImageName.Tenant}-";
            var suffix = ":" + tag;
            if (
                reference is not null
                && reference.StartsWith(prefix, StringComparison.Ordinal)
                && reference.EndsWith(suffix, StringComparison.Ordinal)
                && reference.Length > prefix.Length + suffix.Length
            )
            {
                team = reference[prefix.Length..^suffix.Length];
            }
        }

        team ??= config.DefaultTeam;
        if (squad is null && team is not null)
        {
            try
            {
                var resolved = resolver.ResolveTeam(location, team);
                team = resolved.Name;
                if (resolved.Squads.Count == 1)
                {
                    squad = resolved.Squads[0];
                }
            }
            catch (AboxException)
            {
                // Remote creation does not require local team sources.
            }
        }

        var placeholders =
            team is null || squad is null ? " (replace <name> placeholders)" : string.Empty;
        var local = record is { KitReference: null } ? " --local" : string.Empty;
        var root = !string.IsNullOrWhiteSpace(manifest?.RootDirectory)
            ? $" --root \"{manifest.RootDirectory}\""
            : string.Empty;
        return $"abox create --team {team ?? "<name>"} --squad {squad ?? "<name>"} --tag {tag}{local}{root} --location \"{location}\"{placeholders}";
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
        var record = records.Load(name);
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

        var manifest = store.Load(record.Squad, record.Tag);
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
                    AssumeYes: true,
                    Local: true
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
