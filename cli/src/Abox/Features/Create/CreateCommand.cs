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
                "The squad the sandbox is created from. Without it, the command asks which squad to use.",
        };

        var location = new Option<string>("--location")
        {
            Description =
                "The folder the sandbox works on. Defaults to the location of the user configuration, or the current directory.",
        };

        var tag = new Option<string>("--tag")
        {
            Description =
                "The tag of the kit set and its local build metadata. Defaults to the tag of the user configuration.",
        };

        var local = new Option<bool>("--local")
        {
            Description = "Use the templates that are in sbx, and never pull from the registry.",
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

                        var resolved = resolver.ResolveTeam(
                            Directory.GetCurrentDirectory(),
                            teamName
                        );

                        var request = new CreateRequest(
                            resolved,
                            parseResult.GetValue(squad),
                            parseResult.GetValue(location)
                                ?? config.DefaultLocation
                                ?? Directory.GetCurrentDirectory(),
                            parseResult.GetValue(tag) ?? config.DefaultTag,
                            parseResult.GetValue(assumeYes),
                            Local: parseResult.GetValue(local)
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
