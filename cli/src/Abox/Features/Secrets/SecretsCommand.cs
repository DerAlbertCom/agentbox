using System.CommandLine;
using Abox.Configuration;
using Abox.Health;
using Abox.Infrastructure;

namespace Abox.Features.Secrets;

/// <summary>
/// <c>abox secrets</c>: the secrets of the host that the kits of a squad ask for.
/// </summary>
public sealed class SecretsCommand : Command
{
    public SecretsCommand(
        Lazy<AboxConfig> config,
        Preflight preflight,
        Lazy<SecretsService> secrets,
        IConsole console
    )
        : base("secrets", "Manage the host secrets the kits of a squad ask for.")
    {
        Subcommands.Add(
            SubCommand(
                config,
                preflight,
                console,
                "list",
                "List the services of the squad and whether they are stored.",
                withService: false,
                (request, cancellationToken) => secrets.Value.ListAsync(request, cancellationToken)
            )
        );
        Subcommands.Add(
            SubCommand(
                config,
                preflight,
                console,
                "set",
                "Ask for the secrets of the squad and store them on the host.",
                withService: true,
                (request, cancellationToken) => secrets.Value.SetAsync(request, cancellationToken)
            )
        );
        Subcommands.Add(
            SubCommand(
                config,
                preflight,
                console,
                "remove",
                "Remove the secrets of the squad from the host.",
                withService: true,
                (request, cancellationToken) =>
                    secrets.Value.RemoveAsync(request, cancellationToken)
            )
        );
    }

    private static Command SubCommand(
        Lazy<AboxConfig> config,
        Preflight preflight,
        IConsole console,
        string name,
        string description,
        bool withService,
        Func<SecretsRequest, CancellationToken, Task<int>> action
    )
    {
        var options = new SecretOptions(withService);
        var command = new Command(name, description);
        options.AddTo(command);
        command.SetAction(
            async (parseResult, cancellationToken) =>
                await CommandHandling.RunAsync(
                    async () =>
                    {
                        if (!await preflight.EnsureReadyAsync(cancellationToken))
                            return 1;
                        return await action(
                            options.ReadRequest(parseResult, config),
                            cancellationToken
                        );
                    },
                    console
                )
        );
        return command;
    }

    private sealed class SecretOptions(bool withService)
    {
        private readonly Option<string> team = new("--team")
        {
            Description =
                "The team the squad belongs to. Defaults to the team of the user configuration.",
        };

        private readonly Option<string> squad = new("--squad")
        {
            Description = "The squad whose secrets are managed. The command needs it.",
        };

        private readonly Option<string> root = new("--root")
        {
            Description =
                "The directory of the project that holds the teams folder. Defaults to the current directory.",
        };

        private readonly Option<string[]> service = new("--service")
        {
            Description = "The service to act on. Can be given more than once.",
            AllowMultipleArgumentsPerToken = false,
        };

        public void AddTo(Command command)
        {
            command.Options.Add(team);
            command.Options.Add(squad);
            command.Options.Add(root);

            if (withService)
            {
                command.Options.Add(service);
            }
        }

        public SecretsRequest ReadRequest(ParseResult parseResult, Lazy<AboxConfig> config)
        {
            var wanted = withService ? parseResult.GetValue(service) ?? [] : [];
            if (parseResult.GetValue(squad) is not { } squadName)
                throw new AboxException("No squad is given. Give --squad <name>.");
            var teamName =
                parseResult.GetValue(team)
                ?? config.Value.DefaultTeam
                ?? throw new AboxException(
                    "No team is given. Give --team <name> or set 'defaultTeam' in the user configuration."
                );
            return new SecretsRequest(
                teamName,
                squadName,
                parseResult.GetValue(root) ?? Directory.GetCurrentDirectory(),
                wanted
            );
        }
    }
}
