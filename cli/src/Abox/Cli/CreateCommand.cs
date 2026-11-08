using System.CommandLine;
using Abox.Services;

namespace Abox.Cli;

/// <summary>
/// <c>abox create</c>: creates the sandbox of a folder from the local build of a squad.
/// </summary>
public static class CreateCommand
{
    public static Command Create(AboxServices services)
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

        var command = new Command(
            "create",
            "Create the sandbox of a folder from the local build of a squad."
        )
        {
            Options = { squad, location, tag, localImage, assumeYes },
        };

        command.SetAction(
            async (parseResult, cancellationToken) =>
            {
                var request = new CreateRequest(
                    parseResult.GetValue(squad) ?? services.Config.DefaultSquad,
                    parseResult.GetValue(location)
                        ?? services.Config.DefaultLocation
                        ?? Directory.GetCurrentDirectory(),
                    parseResult.GetValue(tag) ?? services.Config.DefaultTag,
                    parseResult.GetValue(assumeYes)
                );

                return await CommandHandling.RunAsync(
                    async () =>
                    {
                        if (!await services.Preflight.EnsureReadyAsync(cancellationToken))
                        {
                            return 1;
                        }

                        return await services.Create.CreateAsync(request, cancellationToken);
                    },
                    services.Console
                );
            }
        );

        return command;
    }
}
