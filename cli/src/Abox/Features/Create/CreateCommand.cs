using System.CommandLine;
using Abox.Configuration;
using Abox.Health;
using Abox.Infrastructure;

namespace Abox.Features.Create;

/// <summary>
/// <c>abox create</c>: creates the sandbox of a folder from the local build of a squad.
/// </summary>
public sealed class CreateCommand : Command
{
    public CreateCommand(
        AboxConfig config,
        Preflight preflight,
        CreateService create,
        IConsole console
    )
        : base("create", "Create the sandbox of a folder from the local build of a squad.")
    {
        var squad = new Option<string>("--squad")
        {
            Description =
                "The squad the sandbox is created from. Defaults to the squad of the user configuration.",
        };

        var location = new Option<string>("--location")
        {
            Description =
                "The folder the sandbox works on. Defaults to the location of the user configuration, or the current directory.",
        };

        var tag = new Option<string>("--tag")
        {
            Description =
                "The tag of the local build to use. Defaults to the tag of the user configuration.",
        };

        var localImage = new Option<bool>("--local-image")
        {
            Description =
                "Use the local build of the squad. This is the only source in this version.",
        };

        var assumeYes = new Option<bool>("--yes")
        {
            Description = "Replace an existing sandbox without asking.",
        };

        Options.Add(squad);
        Options.Add(location);
        Options.Add(tag);
        Options.Add(localImage);
        Options.Add(assumeYes);

        SetAction(
            async (parseResult, cancellationToken) =>
            {
                var request = new CreateRequest(
                    parseResult.GetValue(squad) ?? config.DefaultSquad,
                    parseResult.GetValue(location)
                        ?? config.DefaultLocation
                        ?? Directory.GetCurrentDirectory(),
                    parseResult.GetValue(tag) ?? config.DefaultTag,
                    parseResult.GetValue(assumeYes)
                );

                return await CommandHandling.RunAsync(
                    async () =>
                    {
                        if (!await preflight.EnsureReadyAsync(cancellationToken))
                        {
                            return 1;
                        }

                        if (!parseResult.GetValue(localImage))
                        {
                            console.WriteError(
                                "note: this version of abox creates sandboxes from local images only."
                            );
                        }

                        return await create.CreateAsync(request, cancellationToken);
                    },
                    console
                );
            }
        );
    }
}
