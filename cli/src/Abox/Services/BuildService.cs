using Abox.Build;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Sbx;

namespace Abox.Services;

/// <summary>What to build.</summary>
/// <param name="Squads">The squads to build.</param>
/// <param name="RootDirectory">The directory the squads' configurations live in.</param>
/// <param name="Tag">The tag the builds are stored under.</param>
/// <param name="Verify">Whether the kits are built and checked with sbx.</param>
public sealed record BuildRequest(
    IReadOnlyList<string> Squads,
    string RootDirectory,
    string Tag,
    bool Verify = true
);

/// <summary>
/// Builds a squad: resolves its configuration, writes the workload kit and the state
/// mixin, checks the kits with sbx and keeps the manifest of the result.
/// </summary>
public sealed class BuildService
{
    private readonly SquadResolver _resolver;
    private readonly WorkloadKitGenerator _workloadGenerator;
    private readonly StateMixinGenerator _stateGenerator;
    private readonly PersonalConfigurations _personalConfigurations;
    private readonly BuildStore _store;
    private readonly ISbxClient _sbx;
    private readonly IConsole _console;

    public BuildService(
        SquadResolver resolver,
        WorkloadKitGenerator workloadGenerator,
        StateMixinGenerator stateGenerator,
        PersonalConfigurations personalConfigurations,
        BuildStore store,
        ISbxClient sbx,
        IConsole console
    )
    {
        _resolver = resolver;
        _workloadGenerator = workloadGenerator;
        _stateGenerator = stateGenerator;
        _personalConfigurations = personalConfigurations;
        _store = store;
        _sbx = sbx;
        _console = console;
    }

    /// <summary>Builds the squads and returns the exit code of the command.</summary>
    public async Task<int> BuildAsync(BuildRequest request, CancellationToken cancellationToken)
    {
        foreach (var squad in request.Squads)
        {
            if (!await BuildSquadAsync(squad, request, cancellationToken))
            {
                return 1;
            }
        }

        return 0;
    }

    private async Task<bool> BuildSquadAsync(
        string squadName,
        BuildRequest request,
        CancellationToken cancellationToken
    )
    {
        var squad = _resolver.Resolve(
            request.RootDirectory,
            squadName,
            _personalConfigurations.Files
        );
        var buildDirectory = _store.DirectoryFor(squad.Name, request.Tag);

        var workloadDirectory = Path.Combine(buildDirectory, WorkloadKitGenerator.KitName);
        _workloadGenerator.Generate(squad.Workload, workloadDirectory);
        _console.WriteLine(
            $"Workload kit '{squad.Workload.Name}' written to '{workloadDirectory}'."
        );

        var stateDirectory = Path.Combine(buildDirectory, StateMixinGenerator.MixinName);
        _stateGenerator.Generate(squad.State, stateDirectory);

        if (request.Verify)
        {
            await VerifyAsync(squad, workloadDirectory, stateDirectory, cancellationToken);
        }

        _store.Save(Manifest(squad, request, workloadDirectory, stateDirectory));

        _console.WriteLine($"Squad '{squad.Name}' built as '{request.Tag}'.");
        _console.WriteLine(
            $"Create a sandbox from it with: abox create --squad {squad.Name} --local-image"
        );

        return true;
    }

    private async Task VerifyAsync(
        ResolvedSquad squad,
        string workloadDirectory,
        string stateDirectory,
        CancellationToken cancellationToken
    )
    {
        var kits = new List<(string Name, string Directory)>
        {
            (squad.Workload.Name, workloadDirectory),
        };

        kits.AddRange(
            squad.Kits.Where(kit => kit.IsLocal).Select(kit => (kit.Name, kit.Directory!))
        );

        if (squad.State.Count > 0)
        {
            kits.Add((StateMixinGenerator.MixinName, stateDirectory));
        }

        foreach (var (name, directory) in kits)
        {
            _console.WriteLine($"Building kit '{name}'...");
            var (exitCode, output) = await _sbx.InspectKitAsync(directory, cancellationToken);
            if (exitCode != 0)
            {
                throw new AboxException(
                    $"The kit '{name}' does not build:{Environment.NewLine}{output.Trim()}"
                );
            }
        }
    }

    private BuildManifest Manifest(
        ResolvedSquad squad,
        BuildRequest request,
        string workloadDirectory,
        string stateDirectory
    )
    {
        var manifest = new BuildManifest
        {
            Squad = squad.Name,
            Tag = request.Tag,
            CreatedAt = DateTimeOffset.Now,
            RootDirectory = squad.RootDirectory,
            WorkloadName = squad.Workload.Name,
            WorkloadKitDirectory = workloadDirectory,
            Template = squad.Workload.Template,
            ConfigFiles = [.. squad.ConfigFiles],
        };

        foreach (var kit in squad.Kits)
        {
            manifest.Kits.Add(
                new ManifestKit
                {
                    Name = kit.Name,
                    Reference = kit.Reference,
                    IsLocal = kit.IsLocal,
                    Arguments = kit.Arguments.ToDictionary(
                        argument => argument.Key,
                        argument => argument.Value
                    ),
                }
            );
        }

        foreach (var server in squad.Mcp)
        {
            manifest.Mcp.Add(new ManifestMcp { Name = server.Name, Url = server.Url });
        }

        foreach (var state in squad.State)
        {
            manifest.State.Add(
                new ManifestState
                {
                    SandboxPath = state.SandboxPath,
                    RelativePath = state.RelativePath,
                }
            );
        }

        manifest.StateMixinDirectory = squad.State.Count > 0 ? stateDirectory : null;

        return manifest;
    }
}
