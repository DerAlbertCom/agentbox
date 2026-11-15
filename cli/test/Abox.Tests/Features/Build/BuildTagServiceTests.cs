using Abox.Configuration;
using Abox.Features.Build;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuildTagServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectBuildRejectsAnInvalidTagEvenWhenNoSquadsAreSelected(bool metadataOnly)
    {
        // Arrange
        using var root = new TempDirectory();
        var paths = AboxPaths.For(
            Path.Combine(root.Path, "config"),
            Path.Combine(root.Path, "state")
        );
        var sbx = Substitute.For<ISbxClient>();
        var runner = Substitute.For<IProcessRunner>();
        var programs = Substitute.For<IHostPrograms>();
        var console = new TestConsole();
        var service = new BuildService(
            new SquadResolver(new AboxConfig()),
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            new PersonalConfigurations(paths),
            new BuildStore(paths),
            new LocalWorkloadImages(programs, new OciRegistry(runner), console),
            sbx,
            console
        );
        var request = new BuildRequest([], root.Path, "../outside", MetadataOnly: metadataOnly);

        // Act
        var build = () => service.BuildAsync(request, CancellationToken.None);

        // Assert
        (await build.Should().ThrowAsync<AboxException>()).WithMessage(
            "*not a valid registry tag*"
        );
        programs.ReceivedCalls().Should().BeEmpty();
        runner.ReceivedCalls().Should().BeEmpty();
        sbx.ReceivedCalls().Should().BeEmpty();
        Directory.Exists(paths.StateDirectory).Should().BeFalse();
    }
}
