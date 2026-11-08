using Abox.Features.Create;
using Abox.Features.Prune;
using Abox.Features.Run;
using Abox.Features.Secrets;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class PruneServiceTests
{
    private const string Assembled = "docker.io/library/sandbox-kit-assembled";
    private const string KitSource = "docker.io/library/sbx-kit-src";

    private readonly TestConsole _console = new();
    private readonly ISbxClient _sbx = Substitute.For<ISbxClient>();
    private readonly PruneService _service;

    public PruneServiceTests()
    {
        _service = new PruneService(_sbx, _console);
        _sbx.RemoveTemplateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
        _sbx.StopSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
    }

    [Fact]
    public async Task NothingToRemoveIsReportedAndReturnsZero()
    {
        _sbx.ListTemplatesAsync(Arg.Any<CancellationToken>()).Returns([]);

        var code = await _service.PruneTemplatesAsync(
            new PruneRequest(false, false),
            CancellationToken.None
        );

        code.Should().Be(0);
        await _sbx.DidNotReceive()
            .RemoveTemplateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _console.AllText.Should().Contain("Nothing to prune");
    }

    [Fact]
    public async Task TheStaleImagesAreRemovedAndThePlanDecidesIt()
    {
        _sbx.ListTemplatesAsync(Arg.Any<CancellationToken>()).Returns(SampleImages());

        var code = await _service.PruneTemplatesAsync(
            new PruneRequest(false, false),
            CancellationToken.None
        );

        code.Should().Be(0);
        await _sbx.Received()
            .RemoveTemplateAsync(
                $"{Assembled}:0307199587cd420e-kit-1",
                Arg.Any<CancellationToken>()
            );
        await _sbx.Received()
            .RemoveTemplateAsync($"{KitSource}:hello-36b64f31e3e9", Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .RemoveTemplateAsync(
                $"{Assembled}:7b7fd6a50dd39472-kit-2",
                Arg.Any<CancellationToken>()
            );
        await _sbx.DidNotReceive()
            .RemoveTemplateAsync(
                $"{KitSource}:pi-config-eacbe0b12b58",
                Arg.Any<CancellationToken>()
            );
        _console.AllText.Should().Contain("Removing 15 stale template images");
    }

    [Fact]
    public async Task AFailedRemovalStopsTheRunWithAnError()
    {
        _sbx.ListTemplatesAsync(Arg.Any<CancellationToken>()).Returns(SampleImages());
        _sbx.RemoveTemplateAsync($"{Assembled}:3e09f411a663a519", Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(1, "no such image"));

        var act = () =>
            _service.PruneTemplatesAsync(new PruneRequest(false, false), CancellationToken.None);

        await act.Should().ThrowAsync<AboxException>().WithMessage("*3e09f411a663a519*");
    }

    [Fact]
    public async Task AFullPruneReachesForEverythingAndKeepsWhatSbxNeeds()
    {
        _sbx.ListTemplatesAsync(Arg.Any<CancellationToken>()).Returns(SampleImages());
        _sbx.RemoveTemplateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(1, "the image is used by a sandbox"));

        var code = await _service.PruneTemplatesAsync(
            new PruneRequest(true, false),
            CancellationToken.None
        );

        code.Should().Be(0);
        await _sbx.Received()
            .RemoveTemplateAsync(
                $"{Assembled}:7b7fd6a50dd39472-kit-2",
                Arg.Any<CancellationToken>()
            );
        await _sbx.Received()
            .RemoveTemplateAsync($"{KitSource}:state-4403f5477cee", Arg.Any<CancellationToken>());
        _console.AllText.Should().Contain("sbx keeps");
    }

    [Fact]
    public async Task AFullPruneStopsTheSandboxesWhenItIsAskedTo()
    {
        _sbx.ListTemplatesAsync(Arg.Any<CancellationToken>()).Returns(SampleImages());
        _sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(
                new List<SandboxInfo>
                {
                    new("a-running-one", "running", DateTimeOffset.Now),
                    new("a-stopped-one", "stopped", DateTimeOffset.Now),
                }
            );

        var code = await _service.PruneTemplatesAsync(
            new PruneRequest(true, true),
            CancellationToken.None
        );

        code.Should().Be(0);
        await _sbx.DidNotReceive().StopSandboxAsync("a-stopped-one", Arg.Any<CancellationToken>());
        await _sbx.Received().StopSandboxAsync("a-running-one", Arg.Any<CancellationToken>());
        _console.AllText.Should().Contain("Stopping 1 running sandbox");
    }

    [Fact]
    public async Task ARegularPruneDoesNotStopTheSandboxes()
    {
        _sbx.ListTemplatesAsync(Arg.Any<CancellationToken>()).Returns(SampleImages());
        _sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(new List<SandboxInfo> { new("a-running-one", "running", DateTimeOffset.Now) });

        var code = await _service.PruneTemplatesAsync(
            new PruneRequest(false, true),
            CancellationToken.None
        );

        code.Should().Be(0);
        await _sbx.Received().StopSandboxAsync("a-running-one", Arg.Any<CancellationToken>());
    }

    /// <summary>One template image, backdated conveniently.</summary>
    private static TemplateImage Template(
        string repository,
        string tag,
        string id,
        string created
    ) => new(repository, tag, id, DateTimeOffset.Parse(created));

    /// <summary>The template images of one real afternoon, in the order sbx lists them.</summary>
    private static List<TemplateImage> SampleImages()
    {
        var images = new List<TemplateImage>();
        images.AddRange(
            Template(Assembled, "0307199587cd420e", "527ef0280e47", "2026-11-08T14:49:51Z")
        );
        images.AddRange(
            Template(Assembled, "0307199587cd420e-kit-0", "64bfab03fc89", "2026-11-08T14:49:51Z")
        );
        images.AddRange(
            Template(Assembled, "0307199587cd420e-kit-1", "ecc48f9d23bd", "2026-11-08T14:55:39Z")
        );
        images.AddRange(
            Template(Assembled, "0307199587cd420e-template", "64bfab03fc89", "2026-11-08T14:49:51Z")
        );
        images.AddRange(
            Template(Assembled, "26cb35b823b12331", "b1f867cc5302", "2026-11-08T15:02:00Z")
        );
        images.AddRange(
            Template(Assembled, "26cb35b823b12331-kit-0", "9ea13c4590bc", "2026-11-08T15:02:00Z")
        );
        images.AddRange(
            Template(Assembled, "26cb35b823b12331-kit-1", "ecc48f9d23bd", "2026-11-08T14:55:39Z")
        );
        images.AddRange(
            Template(Assembled, "26cb35b823b12331-template", "9ea13c4590bc", "2026-11-08T15:02:00Z")
        );
        images.AddRange(
            Template(Assembled, "3e09f411a663a519", "4ec245bdff7f", "2026-09-22T06:14:04Z")
        );
        images.AddRange(
            Template(Assembled, "7b7fd6a50dd39472", "884620d2ac71", "2026-11-08T15:02:00Z")
        );
        images.AddRange(
            Template(Assembled, "7b7fd6a50dd39472-kit-0", "9ea13c4590bc", "2026-11-08T15:02:00Z")
        );
        images.AddRange(
            Template(Assembled, "7b7fd6a50dd39472-kit-1", "c82890ca8c9d", "2026-11-08T15:06:03Z")
        );
        images.AddRange(
            Template(Assembled, "7b7fd6a50dd39472-kit-2", "ecc48f9d23bd", "2026-11-08T14:55:39Z")
        );
        images.AddRange(
            Template(Assembled, "7b7fd6a50dd39472-template", "9ea13c4590bc", "2026-11-08T15:02:00Z")
        );
        images.AddRange(
            Template(KitSource, "elastic-auth-602ed3df56ec", "2a10892e8779", "2026-11-08T14:50:52Z")
        );
        images.AddRange(
            Template(
                KitSource,
                "github-enterprise-auth-784ba46cf335",
                "c2b4c5c6abce",
                "2026-11-08T14:50:51Z"
            )
        );
        images.AddRange(
            Template(KitSource, "hello-36b64f31e3e9", "8046dc73a417", "2026-11-08T14:27:39Z")
        );
        images.AddRange(
            Template(
                KitSource,
                "opencode-config-ec5837b345f0",
                "003b3fbe5ffd",
                "2026-11-08T14:50:51Z"
            )
        );
        images.AddRange(
            Template(KitSource, "pi-config-eacbe0b12b58", "ecc48f9d23bd", "2026-11-08T14:55:39Z")
        );
        images.AddRange(
            Template(KitSource, "state-4403f5477cee", "c82890ca8c9d", "2026-11-08T15:06:03Z")
        );
        images.AddRange(
            Template(KitSource, "state-a6d3285689a3", "b0542b79d94d", "2026-11-08T14:50:52Z")
        );
        images.AddRange(
            Template(KitSource, "workload-21a9f1cddc8a", "9ea13c4590bc", "2026-11-08T15:02:00Z")
        );
        images.AddRange(
            Template(KitSource, "workload-49a52a22791b", "64bfab03fc89", "2026-11-08T14:49:51Z")
        );
        return images;
    }
}
