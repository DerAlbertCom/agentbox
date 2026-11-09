using System.CommandLine;
using Abox.Configuration;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Kits;

namespace Abox.Features.Create;

/// <summary>
/// <c>abox create</c>: creates the sandbox of a folder from a published or local squad kit set.
/// </summary>
public sealed class CreateCommand : Command
{
    public CreateCommand(
        AboxConfig config,
        Preflight preflight,
        SquadResolver resolver,
        CreateService create,
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
        Options.Add(local);
        Options.Add(assumeYes);

        SetAction(
            async (parseResult, cancellationToken) =>
                await CommandHandling.RunAsync(
                    async () =>
                    {
                        var teamName =
                            parseResult.GetValue(team)
                            ?? config.DefaultTeam
                            ?? throw new AboxException(
                                "No team is given. Give --team <name> or set 'defaultTeam' in the user configuration."
                            );

                        var useLocal = parseResult.GetValue(local);
                        var selectedSquad = parseResult.GetValue(squad);
                        if (!useLocal && string.IsNullOrWhiteSpace(selectedSquad))
                        {
                            throw new AboxException(
                                "Remote create requires --squad <name>; published squad discovery is not supported. No local team files are used."
                            );
                        }

                        var resolved = useLocal
                            ? resolver.ResolveTeam(Directory.GetCurrentDirectory(), teamName)
                            : new SquadResolver.ResolvedTeam(teamName, string.Empty, []);

                        var request = new CreateRequest(
                            resolved,
                            selectedSquad,
                            parseResult.GetValue(location)
                                ?? config.DefaultLocation
                                ?? Directory.GetCurrentDirectory(),
                            parseResult.GetValue(tag) ?? config.DefaultTag,
                            parseResult.GetValue(assumeYes),
                            Local: useLocal
                        );

                        if (!await preflight.EnsureReadyAsync(cancellationToken))
                        {
                            return 1;
                        }

                        return await create.CreateAsync(request, cancellationToken);
                    },
                    console
                )
        );
    }
}
