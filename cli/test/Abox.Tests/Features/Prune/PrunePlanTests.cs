using Abox.Features.Prune;
using Abox.Sbx;

namespace Abox.Tests;

public sealed class PrunePlanTests
{
    [Fact]
    public void NormalPruneKeepsEveryNewestGroupIncludingOlderKitsAliasesAndSharedSources()
    {
        // Arrange
        var firstNewest = Image("sandbox-kit-assembled", "new-a", "a", 3);
        var olderKit = Image("sandbox-kit-assembled", "new-a-kit-0", "shared", 1);
        var alias = Image("sandbox-kit-assembled", "new-a-template", "shared", 1);
        var tiedNewest = Image("sandbox-kit-assembled", "new-b", "b", 3);
        var sharedSource = Image("sbx-kit-src", "shared-source", "shared", 1);
        var stale = Image("sandbox-kit-assembled", "old", "old", 2);
        var staleAlias = Image("sandbox-kit-assembled", "old-template", "shared", 1);
        var unused = Image("sbx-kit-src", "unused", "unused", 1);
        TemplateImage[] retained = [firstNewest, olderKit, alias, tiedNewest, sharedSource];

        // Act
        var plan = PruneService.Plan([.. retained, stale, staleAlias, unused]);

        // Assert
        plan.Keep.Should().BeEquivalentTo(retained);
        plan.Remove.Should().BeEquivalentTo<TemplateImage>([stale, staleAlias, unused]);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void UncertainAssembledSetsKeepTheirWholeGroupAndPotentialKitSources(
        bool full,
        bool missingAge
    )
    {
        // Arrange
        var uncertain = Image("sandbox-kit-assembled", "uncertain-kit-0", "uncertain", 1);
        uncertain = missingAge ? uncertain with { CreatedAt = null } : uncertain with { Id = "" };
        var sibling = Image("sandbox-kit-assembled", "uncertain-kit-1", "sibling", 1);
        var alias = Image("sandbox-kit-assembled", "uncertain-template", "sibling", 1);
        var sharedSource = Image("sbx-kit-src", "shared", "sibling", 1);
        var potentialSource = Image("sbx-kit-src", "unknown-dependency", "potential", 1);
        var newest = Image("sandbox-kit-assembled", "new", "new", 3);
        var stale = Image("sandbox-kit-assembled", "stale", "stale", 2);
        TemplateImage[] retained = [uncertain, sibling, alias, sharedSource, potentialSource];
        TemplateImage[] images = [.. retained, newest, stale];

        // Act
        var plan = full ? PruneService.Everything(images) : PruneService.Plan(images);

        // Assert
        plan.Keep.Should().Contain(retained);
        plan.Remove.Should().BeEquivalentTo<TemplateImage>(full ? [newest, stale] : [stale]);
    }

    [Theory]
    [InlineData(false, "sandbox-kit-assembled", false)]
    [InlineData(false, "sandbox-kit-assembled", true)]
    [InlineData(true, "sandbox-kit-assembled", false)]
    [InlineData(true, "sandbox-kit-assembled", true)]
    [InlineData(false, "sbx-kit-src", false)]
    [InlineData(false, "sbx-kit-src", true)]
    [InlineData(true, "sbx-kit-src", false)]
    [InlineData(true, "sbx-kit-src", true)]
    public void MissingAgeOrIdentityPreventsRemoval(bool full, string repository, bool missingAge)
    {
        // Arrange
        var uncertain = Image(repository, "old", "old", 1);
        uncertain = missingAge ? uncertain with { CreatedAt = null } : uncertain with { Id = "" };
        var newest = Image("sandbox-kit-assembled", "new", "new", 2);
        TemplateImage[] images = [uncertain, newest];

        // Act
        var plan = full ? PruneService.Everything(images) : PruneService.Plan(images);

        // Assert
        plan.Keep.Should().Contain(uncertain);
        plan.Remove.Should().NotContain(uncertain);
    }

    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "docker.io/library/")]
    [InlineData(true, "docker.io/library/")]
    [InlineData(false, "registry.example:5000/runtime/")]
    [InlineData(true, "registry.example:5000/runtime/")]
    public void OnlyExactRepositoryNamesOrQualifiedPathComponentsAreEligible(
        bool full,
        string prefix
    )
    {
        // Arrange
        var newest = Image(prefix + "sandbox-kit-assembled", "new", "new", 2);
        var stale = Image(prefix + "sandbox-kit-assembled", "old", "old", 1);
        var source = Image(prefix + "sbx-kit-src", "unused", "unused", 1);
        TemplateImage[] unrelated =
        [
            Image(prefix + "other-sandbox-kit-assembled", "old", "lookalike", 1),
            Image(prefix + "other-sbx-kit-src", "unused", "lookalike-source", 1),
            Image(prefix + "sandbox-kit-assembled-other", "old", "other", 1),
            Image(prefix + "sbx-kit-src-other", "unused", "other-source", 1),
            Image("agentbox-rwest-team/squad", "local", "local", 1),
        ];
        TemplateImage[] images = [newest, stale, source, .. unrelated];

        // Act
        var plan = full ? PruneService.Everything(images) : PruneService.Plan(images);

        // Assert
        plan.Keep.Should().Contain(unrelated);
        plan.Remove.Should()
            .BeEquivalentTo<TemplateImage>(full ? [newest, stale, source] : [stale, source]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GroupAgeIncludesAliasesAndKitsWhilePartitionsPreserveOrderAndDuplicates(bool full)
    {
        // Arrange
        var root = Image("sandbox-kit-assembled", "new-a", "root", 1);
        var alias = Image("sandbox-kit-assembled", "new-a-template", "alias", 4);
        var tiedKit = Image("sandbox-kit-assembled", "new-b-kit-12", "kit", 4);
        var source = Image("sbx-kit-src", "shared", "root", 1);
        var stale = Image("sandbox-kit-assembled", "old", "root", 3);
        var unrelated = Image("other", "new", "other", 5);
        TemplateImage[] images = [stale, root, unrelated, alias, stale, tiedKit, source];

        // Act
        var plan = full ? PruneService.Everything(images) : PruneService.Plan(images);

        // Assert
        plan.Keep.Should().Equal(full ? [unrelated] : [root, unrelated, alias, tiedKit, source]);
        plan.Remove.Should()
            .Equal(full ? [stale, root, alias, stale, tiedKit, source] : [stale, stale]);
    }

    [Theory]
    [InlineData(false, " ")]
    [InlineData(true, " ")]
    [InlineData(false, "\t")]
    [InlineData(true, "\t")]
    [InlineData(false, null)]
    [InlineData(true, null)]
    public void BlankAssembledIdentityProtectsItsAliasesAndEveryPotentialSource(
        bool full,
        string? id
    )
    {
        // Arrange
        var uncertain = Image("sandbox-kit-assembled", "old-kit-10", id!, 1);
        var alias = Image("sandbox-kit-assembled", "old-template", "alias", 1);
        var source = Image("sbx-kit-src", "potential", "source", 1);
        var newest = Image("sandbox-kit-assembled", "new", "new", 2);
        TemplateImage[] images = [uncertain, alias, source, newest];

        // Act
        var plan = full ? PruneService.Everything(images) : PruneService.Plan(images);

        // Assert
        plan.Keep.Should().Equal(full ? [uncertain, alias, source] : images);
        plan.Remove.Should().Equal(full ? [newest] : []);
    }

    private static TemplateImage Image(string repository, string tag, string id, int day) =>
        new(repository, tag, id, new DateTimeOffset(2026, 11, day, 0, 0, 0, TimeSpan.Zero));
}
