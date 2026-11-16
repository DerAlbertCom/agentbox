using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sandboxes;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

public sealed class LocalCreateTagSafetyTests
{
    [Theory]
    [InlineData("resolve")]
    [InlineData("load")]
    [InlineData("create")]
    public async Task DirectLocalCreationRejectsUnsafeTagsBeforeReadingBuildsOrPrompting(
        string operation
    )
    {
        // Arrange
        using var root = new TempDirectory();
        using var outside = new TempDirectory();
        var sentinel = outside.File("build.json", "external manifest must not be read");
        var paths = AboxPaths.For(
            Path.Combine(root.Path, "config"),
            Path.Combine(root.Path, "state")
        );
        var tag = Path.GetRelativePath(Path.Combine(paths.BuildsDirectory, "ac"), outside.Path);
        var config = new AboxConfig();
        var console = new TestConsole();
        var programs = Substitute.For<IHostPrograms>();
        var runner = Substitute.For<IProcessRunner>();
        var sbx = Substitute.For<ISbxClient>();
        var published = Substitute.For<IPublishedSquadSource>();
        var source = new LocalSquadSource(
            new BuildStore(paths),
            new LocalWorkloadImages(programs, new OciRegistry(runner), console),
            console
        );
        var creator = new SandboxCreator(
            new SandboxRecordStore(paths),
            paths,
            SandboxTest.Resolver(sbx),
            sbx,
            console,
            source,
            new RemoteSquadSource(config, sbx, published),
            new UserSecretsCopy(config, new UserSecretsHost(null, null), sbx),
            SandboxTest.Ports(sbx, console)
        );
        var request = new SandboxCreationRequest(
            new SquadResolver.ResolvedTeam("sample", root.Path, ["ac", "bd"]),
            null,
            Path.Combine(root.Path, "missing-location"),
            tag,
            null,
            AssumeYes: true,
            Local: true
        );
        var create = Operation(operation, source, creator, request);

        // Act
        var result = await create.Should().ThrowAsync<AboxException>();

        // Assert
        result.WithMessage("*not a valid registry tag*");
        console
            .Output.Should()
            .BeEmpty("an invalid tag must not prompt for a squad or replacement");
        programs.ReceivedCalls().Should().BeEmpty();
        runner.ReceivedCalls().Should().BeEmpty();
        sbx.ReceivedCalls().Should().BeEmpty();
        published.ReceivedCalls().Should().BeEmpty();
        File.ReadAllText(sentinel).Should().Be("external manifest must not be read");
        Directory.GetFiles(outside.Path, "*", SearchOption.AllDirectories).Should().Equal(sentinel);
        Directory.Exists(paths.StateDirectory).Should().BeFalse();
    }

    private static Func<Task> Operation(
        string operation,
        LocalSquadSource source,
        SandboxCreator creator,
        SandboxCreationRequest request
    ) =>
        operation switch
        {
            "resolve" => () =>
            {
                source.Resolve(request);
                return Task.CompletedTask;
            },
            "load" => async () =>
            {
                await source.LoadAsync(
                    new SquadSelection("sample", "ac", request.Tag),
                    CancellationToken.None
                );
            },
            _ => async () =>
            {
                await creator.CreateAsync(request, CancellationToken.None);
            },
        };
}
