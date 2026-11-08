using System.Text.RegularExpressions;
using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Features.Prune;

/// <summary>What a prune removes and what it keeps.</summary>
/// <param name="Remove">The template images to remove.</param>
/// <param name="Keep">The template images to keep.</param>
public sealed record PrunePlan(
    IReadOnlyList<TemplateImage> Remove,
    IReadOnlyList<TemplateImage> Keep
);

/// <summary>How far a prune goes.</summary>
/// <param name="Everything">Whether it reaches for everything, for the mode of a full prune.</param>
/// <param name="StopSandboxes">Whether it stops the running sandboxes first.</param>
public sealed record PruneRequest(bool Everything, bool StopSandboxes);

/// <summary>
/// Prunes the template images of the local sbx. Keeps the newest assembled kit set
/// and the kit sources it is built from, removes the older sets and the kit sources
/// nothing uses. Never touches sandboxes, secrets, or images of any other repository.
/// </summary>
public sealed partial class PruneService(ISbxClient sbx, IConsole console)
{
    /// <summary>The repository sbx keeps the assembled kit sets in.</summary>
    private const string AssembledRepository = "sandbox-kit-assembled";

    /// <summary>The repository sbx keeps the built kits in.</summary>
    private const string KitSourceRepository = "sbx-kit-src";

    /// <summary>The suffix of the alias tag a build gets for its snapshot.</summary>
    private const string TemplateSuffix = "-template";

    [GeneratedRegex(@"-kit-\d+$")]
    private static partial Regex KitIndexSuffix();

    /// <summary>Prunes the template images and returns the exit code of the command.</summary>
    public async Task<int> PruneTemplatesAsync(
        PruneRequest request,
        CancellationToken cancellationToken
    )
    {
        if (request.StopSandboxes)
        {
            await StopSandboxesAsync(cancellationToken);
        }

        var images = await sbx.ListTemplatesAsync(cancellationToken);
        var plan = request.Everything ? Everything(images) : Plan(images);

        if (plan.Remove.Count == 0)
        {
            console.WriteLine(
                "Nothing to prune: the newest set and its kit sources are all that is left."
            );
            return 0;
        }

        if (request.Everything)
        {
            console.WriteLine(
                $"Reaching for {plan.Remove.Count} template images of sbx. What a sandbox "
                    + "uses, sbx keeps."
            );
        }
        else
        {
            console.WriteLine($"Keeping the newest set '{KeptBuilds(plan)}' and its kit sources.");
            console.WriteLine($"Removing {plan.Remove.Count} stale template images...");
        }

        var failures = new List<string>();
        foreach (var image in plan.Remove)
        {
            var outcome = await sbx.RemoveTemplateAsync(image.Reference, cancellationToken);
            if (outcome.Succeeded)
            {
                console.WriteLine($"Removed {image.Reference}.");
            }
            else if (request.Everything)
            {
                console.WriteLine($"sbx keeps {image.Reference}, it uses it.");
            }
            else
            {
                console.WriteError(outcome.Output.Trim());
                failures.Add(image.Reference);
            }
        }

        if (failures.Count > 0)
        {
            throw new AboxException(
                $"sbx could not remove:{Environment.NewLine}"
                    + string.Join(
                        Environment.NewLine,
                        failures.Select(reference => $"  {reference}")
                    )
            );
        }

        return 0;
    }

    /// <summary>Stops every running sandbox, so a full prune reaches what is not running.</summary>
    private async Task StopSandboxesAsync(CancellationToken cancellationToken)
    {
        var running = (await sbx.ListSandboxesAsync(cancellationToken))
            .Where(sandbox => sandbox.IsRunning)
            .ToList();
        if (running.Count == 0)
        {
            return;
        }

        console.WriteLine($"Stopping {running.Count} running sandbox(es)...");
        foreach (var sandbox in running)
        {
            var outcome = await sbx.StopSandboxAsync(sandbox.Name, cancellationToken);
            if (!outcome.Succeeded)
            {
                throw new AboxException($"sbx could not stop the sandbox '{sandbox.Name}'.");
            }
        }
    }

    /// <summary>
    /// What of the template images is stale: everything that is not the newest
    /// assembled set, not a kit source of a kept set, and of a known age.
    /// </summary>
    public static PrunePlan Plan(IReadOnlyList<TemplateImage> images)
    {
        var builds = new Dictionary<string, List<TemplateImage>>(StringComparer.Ordinal);

        foreach (var image in images)
        {
            if (image.Repository.EndsWith(AssembledRepository, StringComparison.Ordinal))
            {
                var buildId = BuildIdOf(image.Tag);
                if (!builds.TryGetValue(buildId, out var build))
                {
                    build = [];
                    builds.Add(buildId, build);
                }
                build.Add(image);
            }
        }

        var newest =
            builds.Count == 0
                ? DateTimeOffset.MinValue
                : builds.Values.Max(build => NewestAt(build));
        var keptIds = builds
            .Values.Where(build => NewestAt(build) == newest)
            .SelectMany(build => build)
            .Where(image => image.Id.Length > 0)
            .Select(image => image.Id)
            .ToHashSet(StringComparer.Ordinal);

        var keep = new List<TemplateImage>();
        var remove = new List<TemplateImage>();

        foreach (var image in images)
        {
            // An image without an age cannot be judged, so it stays.
            if (image.CreatedAt is null)
            {
                keep.Add(image);
                continue;
            }

            if (image.Repository.EndsWith(AssembledRepository, StringComparison.Ordinal))
            {
                var stays =
                    builds.TryGetValue(BuildIdOf(image.Tag), out var build)
                    && build.Contains(image)
                    && NewestAt(build) == newest;
                (stays ? keep : remove).Add(image);
            }
            else if (
                image.Repository.EndsWith(KitSourceRepository, StringComparison.Ordinal)
                && image.Id.Length > 0
                && !keptIds.Contains(image.Id)
            )
            {
                remove.Add(image);
            }
            else
            {
                keep.Add(image);
            }
        }

        return new PrunePlan(remove, keep);
    }

    /// <summary>
    /// The set of a full prune: every kit image of a known age that sbx holds, kept only
    /// where a sandbox needs it. sbx decides that at removal time, so the prune reaches
    /// for all of them and reports the ones sbx keeps.
    /// </summary>
    public static PrunePlan Everything(IReadOnlyList<TemplateImage> images)
    {
        var keep = new List<TemplateImage>();
        var remove = new List<TemplateImage>();

        foreach (var image in images)
        {
            // An image abox does not know is a sbx kit image, or of a known age, stays.
            if (image.CreatedAt is null || image.Id.Length == 0 || !IsKnownRepository(image))
            {
                keep.Add(image);
            }
            else
            {
                remove.Add(image);
            }
        }

        return new PrunePlan(remove, keep);
    }

    private static bool IsKnownRepository(TemplateImage image) =>
        image.Repository.EndsWith(AssembledRepository, StringComparison.Ordinal)
        || image.Repository.EndsWith(KitSourceRepository, StringComparison.Ordinal);

    /// <summary>The build a tag of the assembled repository belongs to.</summary>
    private static string BuildIdOf(string tag)
    {
        var kit = KitIndexSuffix().Match(tag);
        if (kit.Success)
        {
            return tag[..kit.Index];
        }

        return tag.EndsWith(TemplateSuffix, StringComparison.Ordinal)
            ? tag[..^TemplateSuffix.Length]
            : tag;
    }

    private static DateTimeOffset NewestAt(IReadOnlyList<TemplateImage> build) =>
        build.Max(image => image.CreatedAt ?? DateTimeOffset.MinValue);

    /// <summary>The ids of the sets a plan keeps, for the report.</summary>
    private static string KeptBuilds(PrunePlan plan)
    {
        var kept = plan
            .Keep.Where(image =>
                image.Repository.EndsWith(AssembledRepository, StringComparison.Ordinal)
            )
            .Select(image => BuildIdOf(image.Tag))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        return kept.Count == 0 ? "none" : string.Join(" ", kept);
    }
}
