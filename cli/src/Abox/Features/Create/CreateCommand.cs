using System.CommandLine;
using Abox.Configuration;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Sandboxes;

namespace Abox.Features.Create;

/// <summary>
/// <c>abox create</c>: creates the sandbox of a folder from a published or local squad kit set.
/// </summary>
public sealed class CreateCommand : Command
{
    public CreateCommand(
        Lazy<AboxConfig> config,
        Preflight preflight,
        Lazy<SquadResolver> resolver,
        Lazy<SandboxCreator> create,
        IConsole console
    )
        : base(
            "create",
            "Create the sandbox of a folder from a published squad kit set, or a local build with --local."
        )
    {
        var team = new Option<string>("--team")
        {
            Description =
                "The team the squad belongs to. Defaults to the team of the user configuration.",
        };

        var squad = new Option<string>("--squad")
        {
            Description =
                "The squad the sandbox is created from. Required for remote create; --local can ask which squad to use.",
        };

        var location = new Option<string>("--location")
        {
            Description =
                "The folder the sandbox works on. Defaults to the location of the user configuration, or the current directory.",
        };

        var tag = new Option<string>("--tag")
        {
            Description =
                "The tag of the published set or local build. Defaults to the tag of the user configuration.",
        };

        var root = new Option<string>("--root")
        {
            Description =
                "The directory of the project that holds the teams folder for --local. Defaults to the resolved location.",
        };

        var local = new Option<bool>("--local")
        {
            Description =
                "Use the prebuilt local workload and v3 kits, without pulling a published squad set.",
        };

        var assumeYes = new Option<bool>("--yes")
        {
            Description = "Replace an existing sandbox without asking.",
        };

        Options.Add(team);
        Options.Add(squad);
        Options.Add(location);
        Options.Add(tag);
        Options.Add(root);
        Options.Add(local);
        Options.Add(assumeYes);

        SetAction(
            async (parseResult, cancellationToken) =>
                await CommandHandling.RunAsync(
                    async () =>
                    {
                        var useLocal = parseResult.GetValue(local);
                        var sourceRoot = parseResult.GetValue(root);
                        if (!useLocal && sourceRoot is not null)
                        {
                            throw new AboxException(
                                "--root requires --local; remote create does not use local team sources."
                            );
                        }

                        var settings = config.Value;
                        var teamName =
                            parseResult.GetValue(team)
                            ?? settings.DefaultTeam
                            ?? throw new AboxException(
                                "No team is given. Give --team <name> or set 'defaultTeam' in the user configuration."
                            );

                        var selectedSquad = parseResult.GetValue(squad);
                        if (!useLocal && string.IsNullOrWhiteSpace(selectedSquad))
                        {
                            throw new AboxException(
                                "Remote create requires --squad <name>; published squad discovery is not supported. No local team files are used."
                            );
                        }

                        var resolvedLocation = settings.ResolveLocation(
                            parseResult.GetValue(location)
                        );
                        var resolved = useLocal
                            ? resolver.Value.ResolveTeam(
                                Path.GetFullPath(sourceRoot ?? resolvedLocation),
                                teamName
                            )
                            : new SquadResolver.ResolvedTeam(teamName, string.Empty, []);

                        var request = new SandboxCreationRequest(
                            resolved,
                            selectedSquad,
                            resolvedLocation,
                            parseResult.GetValue(tag) ?? settings.DefaultTag,
                            parseResult.GetValue(assumeYes),
                            Local: useLocal
                        );

                        if (!await preflight.EnsureReadyAsync(cancellationToken))
                        {
                            return 1;
                        }

                        return await create.Value.CreateAsync(request, cancellationToken);
                    },
                    console
                )
        );
    }
}
