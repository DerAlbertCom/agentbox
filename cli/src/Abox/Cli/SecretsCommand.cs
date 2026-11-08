using System.CommandLine;
using Abox.Services;

namespace Abox.Cli;

/// <summary>
/// <c>abox secrets</c>: the secrets of the host that the kits of a squad ask for.
/// </summary>
public static class SecretsCommand
{
    public static Command Create(AboxServices services)
    {
        var command = new Command("secrets", "Manage the host secrets the kits of a squad ask for.")
        {
            Subcommands =
            {
                SubCommand(
                    services,
                    "list",
                    "List the services of the squad and whether they are stored.",
                    withService: false,
                    (request, cancellationToken) =>
                        services.Secrets.ListAsync(request, cancellationToken)
                ),
                SubCommand(
                    services,
                    "set",
                    "Ask for the secrets of the squad and store them on the host.",
                    withService: true,
                    (request, cancellationToken) =>
                        services.Secrets.SetAsync(request, cancellationToken)
                ),
                SubCommand(
                    services,
                    "remove",
                    "Remove the secrets of the squad from the host.",
                    withService: true,
                    (request, cancellationToken) =>
                        services.Secrets.RemoveAsync(request, cancellationToken)
                ),
            },
        };

        return command;
    }

    private static Command SubCommand(
        AboxServices services,
        string name,
        string description,
        bool withService,
        Func<SecretsRequest, CancellationToken, Task<int>> action
    )
    {
        var squad = new Option<string>("--squad")
        {
            Description =
                "The squad whose secrets are meant. Defaults to the squad of the user configuration.",
        };

        var root = new Option<string>("--root")
        {
            Description =
                "The directory the squad's configurations live in. Defaults to the current directory.",
        };

        var service = new Option<string[]>("--service")
        {
            Description = "The service to act on. Can be given more than once.",
            AllowMultipleArgumentsPerToken = false,
        };

        var command = new Command(name, description);
        command.Options.Add(squad);
        command.Options.Add(root);

        if (withService)
        {
            command.Options.Add(service);
        }

        command.SetAction(
            async (parseResult, cancellationToken) =>
                await CommandHandling.RunAsync(
                    async () =>
                    {
                        if (!await services.Preflight.EnsureReadyAsync(cancellationToken))
                        {
                            return 1;
                        }

                        var wanted = withService ? parseResult.GetValue(service) ?? [] : [];

                        var request = new SecretsRequest(
                            parseResult.GetValue(squad) ?? services.Config.DefaultSquad,
                            parseResult.GetValue(root) ?? Directory.GetCurrentDirectory(),
                            wanted
                        );

                        return await action(request, cancellationToken);
                    },
                    services.Console
                )
        );

        return command;
    }
}
