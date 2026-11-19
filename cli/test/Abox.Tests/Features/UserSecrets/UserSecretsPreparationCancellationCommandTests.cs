using Abox.Configuration;
using Abox.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsPreparationCancellationCommandTests
{
    [Fact]
    public async Task CancellationBeforePreparationNeverWritesDestinationsOrChangesSyntheticSources()
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["first","shared"]}""");
        cli.Store("first", "{}");
        cli.Root.File(
            "folder/src/deep/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>shared</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File("folder/src/deep/appsettings.json", """{"Endpoint":"synthetic-only"}""");
        var originals = FileSnapshots.CaptureBytes(cli.Root.Path);
        var folder = await FolderConfig.LoadAsync(cli.Location, CancellationToken.None);
        var copy = cli.Provider.GetRequiredService<UserSecretsCopy>();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var prepare = () => copy.PrepareAsync(folder, false, cancellation.Token);

        // Assert
        await prepare.Should().ThrowAsync<OperationCanceledException>();
        await cli.NoWrites();
        cli.Sbx.ReceivedCalls().Should().BeEmpty();
        FileSnapshots.AssertUnchanged(originals);
    }
}
