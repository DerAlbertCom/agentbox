using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed partial class LegacySandboxInventoryTests
{
    [Fact]
    public async Task PruneStillUsesActualInventoryNamesWithoutLocationResolution()
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        string[] names = [cli.Name, LegacySandboxFixture.Name, "unrelated-running-sandbox"];
        cli.Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(names.Select(name => new SandboxInfo(name, "running", null)).ToList());
        cli.Sbx.ListTemplatesAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<TemplateImage>)[]);
        cli.Sbx.StopSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));

        // Act
        var exitCode = await cli.Invoke("prune", "--sbx");

        // Assert
        exitCode.Should().Be(0);
        foreach (var name in names)
            await cli.Sbx.Received(1).StopSandboxAsync(name, Arg.Any<CancellationToken>());
        await cli
            .Sbx.DidNotReceive()
            .FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cli
            .Sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
