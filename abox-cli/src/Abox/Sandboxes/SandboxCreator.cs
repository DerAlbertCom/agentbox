using Abox.ApplicationPorts;
using Abox.Configuration;
using Abox.Credentials;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sbx;
using Abox.UserSecrets;

namespace Abox.Sandboxes;

/// <summary>What to create.</summary>
/// <param name="Team">The team whose squads the sandbox is created from.</param>
/// <param name="Squad">The squad the sandbox is created from, or null when the command asks for one.</param>
/// <param name="Location">The folder the sandbox works on.</param>
/// <param name="Tag">The tag of the published kit set or local build.</param>
/// <param name="FolderConfig">The folder configuration snapshot loaded by the caller; null means absent.</param>
/// <param name="AssumeYes">Whether the replacement of an existing sandbox is confirmed already.</param>
/// <param name="Local">Whether to use local kits instead of pulling the published set.</param>
/// <param name="ExistingSandbox">The actual identity already selected for an accepted run replacement, if any.</param>
public sealed record SandboxCreationRequest(
    SquadResolver.ResolvedTeam Team,
    string? Squad,
    string Location,
    string Tag,
    FolderConfig? FolderConfig,
    bool AssumeYes = false,
    bool Local = false,
    SandboxInfo? ExistingSandbox = null
);

/// <summary>
/// Creates the sandbox of a folder from its squad's published set or local build.
/// </summary>
public sealed class SandboxCreator(
    SandboxRecordStore records,
    AboxPaths paths,
    FolderSandbox sandboxes,
    ISbxClient sbx,
    IConsole console,
    LocalSquadSource local,
    RemoteSquadSource remote,
    UserSecretsCopy userSecrets,
    AppPorts appPorts,
    SquadCredentials credentials
)
{
    /// <summary>Creates the sandbox and returns the exit code of the command.</summary>
    public async Task<int> CreateAsync(
        SandboxCreationRequest request,
        CancellationToken cancellationToken
    )
    {
        RegistryImageName.ValidateTag(request.Tag);

        var location = Path.GetFullPath(request.Location);
        if (!Directory.Exists(location))
        {
            throw new AboxException($"The folder '{location}' does not exist.");
        }

        var folderConfig = request.FolderConfig;
        request = request with
        {
            Squad = folderConfig?.SelectSquad(request.Squad) ?? request.Squad,
        };
        ISquadSource source = request.Local ? local : remote;
        var selection = source.Resolve(request);
        var preparedSecrets = await userSecrets.PrepareAsync(
            folderConfig,
            force: false,
            cancellationToken
        );
        var preparedPorts = await AppPorts.PrepareAsync(
            location,
            FolderConfig.SelectPortOffset(folderConfig),
            cancellationToken
        );
        var prepared = await source.LoadAsync(selection, cancellationToken);
        var manifest = prepared.Metadata;
        var state =
            manifest
                .State?.Select(entry =>
                    StateEntry.FromStored(entry?.SandboxPath, entry?.RelativePath)
                )
                .ToList()
            ?? throw new AboxException(
                "Missing stored state declarations. Rebuild or republish the squad."
            );
        StateEntry.EnsureDisjoint(state);

        var mcpToRegister = await FindMcpServersToRegisterAsync(manifest, cancellationToken);
        var existing =
            request.ExistingSandbox ?? await sandboxes.FindAsync(location, cancellationToken);
        var name = existing?.Name ?? SandboxName.For(location);
        var replace = ConfirmReplacement(existing, location, request.AssumeYes);
        cancellationToken.ThrowIfCancellationRequested();
        await credentials.PrepareAsync(
            request.Team.RootDirectory,
            request.Team.Name,
            selection.Identity.Squad,
            cancellationToken
        );
        cancellationToken.ThrowIfCancellationRequested();
        await RegisterMcpServersAsync(mcpToRegister, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        var mounts = CreateStateDirectories(name, state);
        cancellationToken.ThrowIfCancellationRequested();
        if (replace)
        {
            await RemoveExistingSandboxAsync(name, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var outcome = await sbx.CreateAsync(
            new SbxCreateRequest(
                prepared.KitSource,
                location,
                name,
                prepared.Mixins,
                prepared.Arguments,
                mounts,
                manifest.Mcp.Select(server => server.Name).ToList(),
                PullPolicy: prepared.PullPolicy,
                HostDockerContext: prepared.DockerContext
            ),
            cancellationToken
        );

        if (!outcome.Succeeded)
        {
            throw new AboxException(
                $"sbx could not create the sandbox '{name}'.{Environment.NewLine}{outcome.Output.Trim()}"
            );
        }

        WriteNotes(outcome);

        records.Save(
            name,
            new SandboxRecord
            {
                Team = request.Team.Name,
                Squad = selection.Identity.Squad,
                Tag = request.Tag,
                BuildCreatedAt = manifest.CreatedAt,
                CreatedAt = DateTimeOffset.Now,
                KitReference = prepared.KitReference,
            }
        );

        if (folderConfig is not null)
        {
            await CopyUserSecretsAsync(preparedSecrets, preparedPorts, name, cancellationToken);
        }

        await ConfigureApplicationPortsAsync(preparedPorts, name, cancellationToken);

        console.WriteLine($"Created sandbox '{name}' for '{location}'.");
        console.WriteLine($"Open it with: abox run --location {location}");
        return 0;
    }

    private async Task CopyUserSecretsAsync(
        PreparedUserSecrets preparedSecrets,
        PreparedAppPorts preparedPorts,
        string name,
        CancellationToken cancellationToken
    )
    {
        var recovery =
            "The sandbox was created and its record retained, but required user-secret copying did not complete. "
            + "Completed files may remain replaced; no cross-store rollback is attempted. "
            + $"Fix the folder configuration or destination/runtime and retry without recreating: abox user-secrets --location \"{preparedPorts.Location}\". "
            + $"Application-port/HTTPS setup has not started; after copying succeeds run: abox ports --location \"{preparedPorts.Location}\" --offset {preparedPorts.Offset}";
        try
        {
            var report = await userSecrets.CopyAsync(preparedSecrets, name, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            UserSecretsReportWriter.WriteStores(report, console);
        }
        catch (OperationCanceledException)
        {
            console.WriteError(recovery);
            throw;
        }
        catch (AboxException exception)
        {
            throw new AboxException($"{exception.Message}{Environment.NewLine}{recovery}");
        }
    }

    private async Task ConfigureApplicationPortsAsync(
        PreparedAppPorts prepared,
        string name,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await appPorts.PublishAsync(prepared, name, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (exception is AboxException or OperationCanceledException)
        {
            console.WriteError(
                "The sandbox was created and its record retained, but required application-port/HTTPS setup did not complete. "
                    + "Completed user-secret copies, mappings, relays or certificate configuration may remain; no rollback is attempted. "
                    + $"Fix the reported problem and retry without recreating: abox ports --location \"{prepared.Location}\" --offset {prepared.Offset}"
            );
            throw;
        }
    }

    private bool ConfirmReplacement(SandboxInfo? existing, string location, bool assumeYes)
    {
        if (existing is null)
        {
            return false;
        }

        if (!assumeYes)
        {
            console.WriteLine($"The sandbox '{existing.Name}' already exists for '{location}'.");
            console.WriteLine("Replacing it loses what is inside it:");
            console.WriteLine("  - the container, with everything in it");
            console.WriteLine("  - the worktrees under ~/worktrees");
            console.WriteLine("The state directories on the host are kept.");
            console.WriteLine();

            if (!console.Confirm("Replace the sandbox?"))
            {
                throw new AboxException("The sandbox was not replaced.");
            }
        }

        return true;
    }

    private async Task RemoveExistingSandboxAsync(string name, CancellationToken cancellationToken)
    {
        console.WriteLine($"Removing the sandbox '{name}'...");
        var removal = await sbx.RemoveSandboxAsync(name, cancellationToken);
        if (!removal.Succeeded)
        {
            throw new AboxException(
                $"sbx could not remove the sandbox '{name}'." + Detail(removal)
            );
        }
    }

    private IReadOnlyList<SbxMount> CreateStateDirectories(
        string name,
        IReadOnlyList<StateEntry> state
    )
    {
        var mounts = new List<SbxMount>();

        foreach (var entry in state)
        {
            var hostPath = Path.Combine(paths.StateDirectory, name, entry.RelativePath);
            try
            {
                Directory.CreateDirectory(hostPath);
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                throw new AboxException(
                    $"Cannot prepare the state directory '{hostPath}'. Check its permissions and remove any conflicting file. The existing sandbox was not replaced.",
                    exception
                );
            }
            mounts.Add(new SbxMount(hostPath, entry.SandboxPath));
        }

        return mounts;
    }

    private async Task<IReadOnlyList<ManifestMcp>> FindMcpServersToRegisterAsync(
        BuildManifest manifest,
        CancellationToken cancellationToken
    )
    {
        if (manifest.Mcp.Count == 0)
        {
            return [];
        }

        foreach (var server in manifest.Mcp)
            new McpDeclaration(server.Name, server.Url).Validate($"squad {manifest.Squad}");
        var registered = (await sbx.ListMcpServersAsync(cancellationToken)).ToHashSet(
            StringComparer.Ordinal
        );
        var pending = new List<ManifestMcp>();

        foreach (var server in manifest.Mcp)
        {
            if (server.Url is null)
            {
                if (!registered.Contains(server.Name))
                {
                    throw new AboxException(
                        $"The MCP server '{server.Name}' is not registered on this host. "
                            + $"Register it with 'sbx mcp add {server.Name} --url <url>', or give it an url in the configuration."
                    );
                }
            }
            else if (!registered.Contains(server.Name))
            {
                pending.Add(server);
            }
        }

        return pending;
    }

    private async Task RegisterMcpServersAsync(
        IReadOnlyList<ManifestMcp> servers,
        CancellationToken cancellationToken
    )
    {
        foreach (var server in servers)
        {
            console.WriteLine($"Registering the MCP server '{server.Name}'...");
            var outcome = await sbx.AddMcpServerAsync(server.Name, server.Url!, cancellationToken);
            if (!outcome.Succeeded)
            {
                throw new AboxException(
                    $"The MCP server '{server.Name}' could not be registered." + Detail(outcome)
                );
            }
        }
    }

    /// <summary>The notes of sbx, such as a credential it did not inject; its other output stays captured.</summary>
    private void WriteNotes(SbxOutcome outcome)
    {
        foreach (var line in outcome.Output.Split('\n'))
        {
            var note = line.Trim();
            if (note.StartsWith("Note:", StringComparison.Ordinal))
            {
                console.WriteLine(note);
            }
        }
    }

    private static string Detail(SbxOutcome outcome)
    {
        var output = outcome.Output.Trim();
        return output.Length > 0 ? $" {output}" : string.Empty;
    }
}
