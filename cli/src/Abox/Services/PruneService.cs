using System.Text.RegularExpressions;
using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Services;

/// <summary>What a prune removes and what it keeps.</summary>
/// <param name="Remove">The template images to remove.</param>
/// <param name="Keep">The template images to keep.</param>
public sealed record PrunePlan(
    IReadOnlyList<TemplateImage> Remove,
    IReadOnlyList<TemplateImage> Keep
);

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
    public async Task<int> PruneTemplatesAsync(CancellationToken cancellationToken)
    {
        var plan = Plan(await sbx.ListTemplatesAsync(cancellationToken));

        if (plan.Remove.Count == 0)
        {
            console.WriteLine(
                "Nothing to prune: the newest set and its kit sources are all that is left."
            );
            return 0;
        }

        console.WriteLine($"Keeping the newest set '{KeptBuilds(plan)}' and its kit sources.");
        console.WriteLine($"Removing {plan.Remove.Count} stale template images...");

        var failures = new List<string>();
        foreach (var image in plan.Remove)
        {
            var outcome = await sbx.RemoveTemplateAsync(image.Reference, cancellationToken);
            if (outcome.Succeeded)
            {
                console.WriteLine($"Removed {image.Reference}.");
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
