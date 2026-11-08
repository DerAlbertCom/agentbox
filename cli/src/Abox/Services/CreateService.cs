using Abox.Build;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;

namespace Abox.Services;

/// <summary>What to create.</summary>
/// <param name="Squad">The squad the sandbox is created from.</param>
/// <param name="Location">The folder the sandbox works on.</param>
/// <param name="Tag">The tag of the local build to use.</param>
/// <param name="AssumeYes">Whether the replacement of an existing sandbox is confirmed already.</param>
public sealed record CreateRequest(
    string Squad,
    string Location,
    string Tag,
    bool AssumeYes = false
);

/// <summary>
/// Creates the sandbox of a folder from the local build of its squad.
/// </summary>
public sealed class CreateService(
    BuildStore store,
    SandboxRecordStore records,
    AboxPaths paths,
    ISbxClient sbx,
    IConsole console
)
{
    /// <summary>Creates the sandbox and returns the exit code of the command.</summary>
    public async Task<int> CreateAsync(CreateRequest request, CancellationToken cancellationToken)
    {
        var manifest =
            store.Load(request.Squad, request.Tag)
            ?? throw new AboxException(
                $"There is no local build of '{request.Squad}' with the tag '{request.Tag}'. "
                    + $"Build it first: abox build --squad {request.Squad}"
            );

        var location = Path.GetFullPath(request.Location);
        if (!Directory.Exists(location))
        {
            throw new AboxException($"The folder '{location}' does not exist.");
        }

        var name = SandboxName.For(location);
        await ReplaceExistingSandboxAsync(name, location, request.AssumeYes, cancellationToken);

        var mounts = CreateStateDirectories(name, manifest);
        var servers = await EnsureMcpServersAsync(manifest, cancellationToken);

        var mixins = manifest.Kits.Select(kit => kit.Reference).ToList();
        if (manifest.StateMixinDirectory is not null)
        {
            mixins.Add(manifest.StateMixinDirectory);
        }

        var arguments = manifest
            .Kits.SelectMany(kit => kit.Arguments)
            .GroupBy(argument => argument.Key, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToDictionary(group => group.Key, group => group.Value, StringComparer.Ordinal);

        var outcome = await sbx.CreateAsync(
            new SbxCreateRequest(
                manifest.WorkloadKitDirectory,
                location,
                name,
                mixins,
                arguments,
                mounts,
                servers
            ),
            cancellationToken
        );

        if (!outcome.Succeeded)
        {
            throw new AboxException(
                $"sbx could not create the sandbox '{name}'.{Environment.NewLine}{outcome.Output.Trim()}"
            );
        }

        records.Save(
            name,
            new SandboxRecord
            {
                Squad = request.Squad,
                Tag = request.Tag,
                BuildCreatedAt = manifest.CreatedAt,
                CreatedAt = DateTimeOffset.Now,
            }
        );

        console.WriteLine($"Created sandbox '{name}' for '{location}'.");
        console.WriteLine($"Open it with: abox run --location {location}");
        return 0;
    }

    private async Task ReplaceExistingSandboxAsync(
        string name,
        string location,
        bool assumeYes,
        CancellationToken cancellationToken
    )
    {
        var existing = await sbx.FindSandboxAsync(name, cancellationToken);
        if (existing is null)
        {
            return;
        }

        if (!assumeYes)
        {
            console.WriteLine($"The sandbox '{name}' already exists for '{location}'.");
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

        console.WriteLine($"Removing the sandbox '{name}'...");
        var removal = await sbx.RemoveSandboxAsync(name, cancellationToken);
        if (!removal.Succeeded)
        {
            throw new AboxException(
                $"sbx could not remove the sandbox '{name}'." + Detail(removal)
            );
        }
    }

    private IReadOnlyList<SbxMount> CreateStateDirectories(string name, BuildManifest manifest)
    {
        var mounts = new List<SbxMount>();

        foreach (var state in manifest.State)
        {
            var hostPath = Path.Combine(paths.StateDirectory, name, state.RelativePath);
            Directory.CreateDirectory(hostPath);
            mounts.Add(new SbxMount(hostPath, state.SandboxPath));
        }

        return mounts;
    }

    private async Task<IReadOnlyList<string>> EnsureMcpServersAsync(
        BuildManifest manifest,
        CancellationToken cancellationToken
    )
    {
        if (manifest.Mcp.Count == 0)
        {
            return [];
        }

        var registered = (await sbx.ListMcpServersAsync(cancellationToken)).ToHashSet(
            StringComparer.Ordinal
        );
        var servers = new List<string>();

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
                console.WriteLine($"Registering the MCP server '{server.Name}'...");
                var outcome = await sbx.AddMcpServerAsync(
                    server.Name,
                    server.Url,
                    cancellationToken
                );
                if (!outcome.Succeeded)
                {
                    throw new AboxException(
                        $"The MCP server '{server.Name}' could not be registered." + Detail(outcome)
                    );
                }
            }

            servers.Add(server.Name);
        }

        return servers;
    }

    private static string Detail(SbxOutcome outcome)
    {
        var output = outcome.Output.Trim();
        return output.Length > 0 ? $" {output}" : string.Empty;
    }
}
