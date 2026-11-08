using System.CommandLine;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Services;

namespace Abox.Cli;

/// <summary>
/// <c>abox prune</c>: removes the sbx template images that stopped being used.
/// </summary>
public static class PruneCommand
{
    public static Command Create(Preflight preflight, PruneService prune, IConsole console)
    {
        var command = new Command(
            "prune",
            "Remove the stale template images of sbx: everything but the newest set and its kit sources."
        );

        command.SetAction(
            async (_, cancellationToken) =>
            {
                return await CommandHandling.RunAsync(
                    async () =>
                    {
                        if (!await preflight.EnsureReadyAsync(cancellationToken))
                        {
                            return 1;
                        }

                        return await prune.PruneTemplatesAsync(cancellationToken);
                    },
                    console
                );
            }
        );

        return command;
    }
}
