using System.Collections.Frozen;
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
                "Nothing to prune: no template images are proven eligible for removal."
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
            console.WriteLine(
                $"Keeping assembled sets '{KeptBuilds(plan)}' and their kit sources."
            );
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
    /// assembled set, not an uncertain set or its potential kit sources, and of a known age and id.
    /// </summary>
    public static PrunePlan Plan(IReadOnlyList<TemplateImage> images) =>
        Classify(images, everything: false);

    /// <summary>
    /// The set of a full prune: every kit image of a known age and id, except uncertain
    /// assembled sets and their potential kit sources. sbx refuses images a sandbox needs
    /// at removal time; the command reports those refusals without failing.
    /// </summary>
    public static PrunePlan Everything(IReadOnlyList<TemplateImage> images) =>
        Classify(images, everything: true);

    private static PrunePlan Classify(IReadOnlyList<TemplateImage> images, bool everything)
    {
        var facts = RetentionFactsOf(images);
        var keep = new List<TemplateImage>();
        var remove = new List<TemplateImage>();
        foreach (var image in images)
        {
            (ShouldKeep(image, facts, everything) ? keep : remove).Add(image);
        }
        return new PrunePlan(remove, keep);
    }

    private static bool ShouldKeep(TemplateImage image, RetentionFacts facts, bool everything)
    {
        // Full prune does not override uncertain identity, age or dependencies.
        if (!HasKnownMetadata(image))
            return true;

        if (IsRepository(image, AssembledRepository))
        {
            var group = facts.Builds[BuildIdOf(image.Tag)];
            return group.IsUncertain || (!everything && group.NewestAt == facts.NewestAt);
        }

        if (IsRepository(image, KitSourceRepository))
        {
            // An uncertain assembled group may depend on any source, not only a shared id.
            return facts.HasUncertainBuilds
                || (!everything && facts.NewestImageIds.Contains(image.Id));
        }

        return true;
    }

    private sealed record BuildFacts(
        DateTimeOffset NewestAt,
        bool IsUncertain,
        FrozenSet<string> ImageIds
    );

    private sealed record RetentionFacts(
        FrozenDictionary<string, BuildFacts> Builds,
        DateTimeOffset NewestAt,
        FrozenSet<string> NewestImageIds,
        bool HasUncertainBuilds
    );

    private static RetentionFacts RetentionFactsOf(IReadOnlyList<TemplateImage> images)
    {
        var builds = images
            .Where(image => IsRepository(image, AssembledRepository))
            .GroupBy(image => BuildIdOf(image.Tag), StringComparer.Ordinal)
            .ToFrozenDictionary(group => group.Key, SummarizeBuild, StringComparer.Ordinal);
        var newest =
            builds.Count == 0
                ? DateTimeOffset.MinValue
                : builds.Values.Max(build => build.NewestAt);
        var newestIds = builds
            .Values.Where(build => build.NewestAt == newest)
            .SelectMany(build => build.ImageIds)
            .ToFrozenSet(StringComparer.Ordinal);
        return new RetentionFacts(
            builds,
            newest,
            newestIds,
            builds.Values.Any(build => build.IsUncertain)
        );
    }

    private static BuildFacts SummarizeBuild(IEnumerable<TemplateImage> images)
    {
        var newest = DateTimeOffset.MinValue;
        var uncertain = false;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var image in images)
        {
            if (image.CreatedAt is { } createdAt && createdAt > newest)
                newest = createdAt;
            uncertain |= !HasKnownMetadata(image);
            if (!string.IsNullOrEmpty(image.Id))
                ids.Add(image.Id);
        }
        return new BuildFacts(newest, uncertain, ids.ToFrozenSet(StringComparer.Ordinal));
    }

    private static bool IsRepository(TemplateImage image, string repository) =>
        image.Repository.Equals(repository, StringComparison.Ordinal)
        || image.Repository.EndsWith("/" + repository, StringComparison.Ordinal);

    private static bool HasKnownMetadata(TemplateImage image) =>
        image.CreatedAt is not null && !string.IsNullOrWhiteSpace(image.Id);

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

    /// <summary>The ids of the sets a plan keeps, for the report.</summary>
    private static string KeptBuilds(PrunePlan plan)
    {
        var kept = plan
            .Keep.Where(image => IsRepository(image, AssembledRepository))
            .Select(image => BuildIdOf(image.Tag))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();
        return kept.Count == 0 ? "none" : string.Join(" ", kept);
    }
}
