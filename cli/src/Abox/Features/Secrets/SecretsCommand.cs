using System.CommandLine;
using Abox.Health;
using Abox.Infrastructure;

namespace Abox.Features.Secrets;

/// <summary>
/// <c>abox secrets</c>: the secrets of the host that the kits of a squad ask for.
/// </summary>
public sealed class SecretsCommand : Command
{
    public SecretsCommand(Preflight preflight, SecretsService secrets, IConsole console)
        : base("secrets", "Manage the host secrets the kits of a squad ask for.")
    {
        Subcommands.Add(
            SubCommand(
                preflight,
                console,
                "list",
                "List the services of the squad and whether they are stored.",
                withService: false,
                (request, cancellationToken) => secrets.ListAsync(request, cancellationToken)
            )
        );
        Subcommands.Add(
            SubCommand(
                preflight,
                console,
                "set",
                "Ask for the secrets of the squad and store them on the host.",
                withService: true,
                (request, cancellationToken) => secrets.SetAsync(request, cancellationToken)
            )
        );
        Subcommands.Add(
            SubCommand(
                preflight,
                console,
                "remove",
                "Remove the secrets of the squad from the host.",
                withService: true,
                (request, cancellationToken) => secrets.RemoveAsync(request, cancellationToken)
            )
        );
    }

    private static Command SubCommand(
        Preflight preflight,
        IConsole console,
        string name,
        string description,
        bool withService,
        Func<SecretsRequest, CancellationToken, Task<int>> action
    )
    {
        var squad = new Option<string>("--squad")
        {
            Description = "The squad whose secrets are managed. The command needs it.",
        };

        var root = new Option<string>("--root")
        {
            Description =
                "The directory of the project that holds the teams folder. Defaults to the current directory.",
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
                        if (!await preflight.EnsureReadyAsync(cancellationToken))
                        {
                            return 1;
                        }

                        var wanted = withService ? parseResult.GetValue(service) ?? [] : [];

                        if (parseResult.GetValue(squad) is not { } squadName)
                        {
                            throw new AboxException("No squad is given. Give --squad <name>.");
                        }

                        var request = new SecretsRequest(
                            squadName,
                            parseResult.GetValue(root) ?? Directory.GetCurrentDirectory(),
                            wanted
                        );

                        return await action(request, cancellationToken);
                    },
                    console
                )
        );

        return command;
    }
}
