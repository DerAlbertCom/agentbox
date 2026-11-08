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
        var full = new Option<bool>("--full")
        {
            Description =
                "Reach for everything sbx holds, not only the stale sets. What a sandbox uses, sbx keeps.",
        };

        var stop = new Option<bool>("--sbx")
        {
            Description = "Stop the running sandboxes before the prune.",
        };

        Options.Add(full);
        Options.Add(stop);

        SetAction(
            async (parseResult, cancellationToken) =>
            {
                var request = new PruneRequest(
                    parseResult.GetValue(full),
                    parseResult.GetValue(stop)
                );

                return await CommandHandling.RunAsync(
                    async () =>
                    {
                        if (!await preflight.EnsureReadyAsync(cancellationToken))
                        {
                            return 1;
                        }

                        return await prune.PruneTemplatesAsync(request, cancellationToken);
                    },
                    console
                );
            }
        );
    }
}
