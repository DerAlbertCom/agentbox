using Abox.Build;
using Abox.Configuration;
using Abox.Features.Build;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuildServiceTests
{
    private readonly TempDirectory _root = new();
    private readonly ISbxClient _sbx = Substitute.For<ISbxClient>();
    private readonly BuildStore _store;
    private readonly SquadResolver _resolver = new(new AboxConfig());
    private readonly BuildService _service;

    public BuildServiceTests()
    {
        var paths = AboxPaths.For(
            Path.Combine(_root.Path, "config"),
            Path.Combine(_root.Path, "state")
        );
        _store = new BuildStore(paths);
        _root.File("teams/sample/ac.abox", "workload: sample\n");
        _root.File("teams/sample/sample.workload", "template: example.com/base:1\n");
        _root.File("config/personal.abox", "kits: [private]\n");
        _root.File("kits/private/private.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        _sbx.InspectKitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        _service = new BuildService(
            _resolver,
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            new PersonalConfigurations(paths),
            _store,
            _sbx,
            new TestConsole()
        );
    }

    [Fact]
    public async Task EachBuildKeepsTheRepositoryOfItsInheritedWorkload()
    {
        _root.File("teams/sample/ac.abox", "base: cape\n");
        _root.File("teams/other/cape.abox", "workload: shared\n");
        _root.File("kits/shared.workload", "publishRepository: shared-images-local\n");
        _root.File("teams/sample/bd.abox", "workload: sample\n");
        _root.File("teams/sample/sample.workload", "publishRepository: sample-images-local\n");

        await _service.BuildAsync(
            Request() with
            {
                IncludePersonalConfigurations = false,
            },
            CancellationToken.None
        );

        _store.Load("ac", "latest")!.PublishRepository.Should().Be("shared-images-local");
        _store.Load("bd", "latest")!.PublishRepository.Should().Be("sample-images-local");
    }

    [Fact]
    public async Task ChangingTheWorkloadRepositoryRequiresRebuildingBeforePublication()
    {
        _root.File("teams/sample/sample.workload", "publishRepository: original-images-local\n");
        await _service.BuildAsync(
            Request() with
            {
                IncludePersonalConfigurations = false,
            },
            CancellationToken.None
        );
        _root.File("teams/sample/sample.workload", "publishRepository: other-images-local\n");

        var check = () => BuildSources.EnsureUnchanged(_store.Load("ac", "latest")!);

        check.Should().Throw<AboxException>().WithMessage("*sources changed*--no-personal*");
    }

    [Fact]
    public async Task PublicationBuildsExplicitlyExcludePersonalConfiguration()
    {
        await _service.BuildAsync(
            Request() with
            {
                IncludePersonalConfigurations = false,
            },
            CancellationToken.None
        );

        var manifest = _store.Load("ac", "latest")!;
        manifest.IncludesPersonalConfigurations.Should().BeFalse();
        manifest.Kits.Should().BeEmpty();
        manifest.ConfigFiles.Should().NotContain(file => file.Contains("personal.abox"));
        manifest.SourceHashes.Should().NotBeEmpty();
        BuildSources.EnsureUnchanged(manifest);
    }

    [Fact]
    public async Task LocalBuildsStillIncludePersonalConfigurationByDefaultAndRecordIt()
    {
        await _service.BuildAsync(Request(), CancellationToken.None);

        var manifest = _store.Load("ac", "latest")!;
        manifest.IncludesPersonalConfigurations.Should().BeTrue();
        manifest.Kits.Should().ContainSingle().Which.Name.Should().Be("private");
        manifest.SourceHashes.Should().BeEmpty();
    }

    [Theory(SkipExceptions = [typeof(LinkUnavailableException)])]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OnlyPublicationBuildsEnforceLinkFreeInputs(bool includePersonalConfigurations)
    {
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [private]\n");
        File.Delete(Path.Combine(_root.Path, "config", "personal.abox"));
        var target = _root.Directory_("shared");
        _root.File("shared/payload.txt", "linked kit payload");
        var link = Path.Combine(_root.Path, "kits", "private", "linked");
        try
        {
            if (OperatingSystem.IsWindows())
            {
                var result = await new ProcessRunner().RunAsync(
                    new ProcessRequest("cmd", ["/c", "mklink", "/J", link, target]),
                    CancellationToken.None
                );
                if (!result.Succeeded)
                {
                    throw new LinkUnavailableException(
                        $"Directory junctions are not available: {result.StandardError}"
                    );
                }
            }
            else
            {
                Directory.CreateSymbolicLink(link, target);
            }
        }
        catch (Exception exception)
            when (exception
                    is UnauthorizedAccessException
                        or PlatformNotSupportedException
                        or IOException
            )
        {
            throw new LinkUnavailableException(
                $"Symbolic links are not available on this host: {exception.Message}"
            );
        }

        var request = Request() with
        {
            IncludePersonalConfigurations = includePersonalConfigurations,
        };
        if (includePersonalConfigurations)
        {
            var exitCode = await _service.BuildAsync(request, CancellationToken.None);

            exitCode.Should().Be(0);
            _store.Load("ac", "latest")!.SourceHashes.Should().BeEmpty();
            await _sbx.Received(1)
                .InspectKitAsync(
                    Path.Combine(_root.Path, "kits", "private"),
                    Arg.Any<CancellationToken>()
                );
        }
        else
        {
            var action = () => _service.BuildAsync(request, CancellationToken.None);

            (await action.Should().ThrowAsync<AboxException>()).WithMessage(
                "*symbolic link or reparse point*"
            );
            _store.Load("ac", "latest").Should().BeNull();
        }
    }

    [Fact]
    public async Task SourcesChangedDuringVerificationAreNotSavedAsAVerifiedBuild()
    {
        _sbx.InspectKitAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                File.AppendAllText(
                    Path.Combine(call.Arg<string>(), "workload.yaml"),
                    "description: changed during verification\n"
                );
                return new SbxOutcome(0, "");
            });

        var action = () =>
            _service.BuildAsync(
                Request() with
                {
                    IncludePersonalConfigurations = false,
                },
                CancellationToken.None
            );

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*sources changed*");
        _store.Load("ac", "latest").Should().BeNull();
    }

    private BuildRequest Request() =>
        new([_resolver.ResolveTeam(_root.Path, "sample")], _root.Path, "latest");

    private sealed class LinkUnavailableException(string message) : Exception(message);
}
