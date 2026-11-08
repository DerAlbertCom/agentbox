using System.CommandLine;
using Abox.Health;
using Abox.Infrastructure;

namespace Abox.Features.Prune;

/// <summary>
/// <c>abox prune</c>: removes the sbx template images that stopped being used.
/// </summary>
public sealed class PruneCommand : Command
{
    public PruneCommand(Preflight preflight, PruneService prune, IConsole console)
        : base(
            "prune",
            "Remove the stale template images of sbx: everything but the newest set and its kit sources."
        )
    {
        SetAction(
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
    }
}
