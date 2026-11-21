using Abox.Configuration;

namespace Abox.Tests;

public sealed class FolderConfigTests
{
    [Theory]
    [InlineData(".")]
    [InlineData("absent")]
    public async Task MissingFileOrDirectoryIsAnAbsentSnapshot(string relativeLocation)
    {
        // Arrange
        using var root = new TempDirectory();
        var location = Path.Combine(root.Path, relativeLocation);

        // Act
        var snapshot = await FolderConfig.LoadAsync(location, CancellationToken.None);

        // Assert
        snapshot.Should().BeNull();
    }

    [Fact]
    public async Task CancellationIsNotTranslatedIntoAnInvalidConfigurationError()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("abox.json", "{}");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Act
        var load = () => FolderConfig.LoadAsync(root.Path, cancellation.Token);

        // Assert
        await load.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task AnUnreadableConfigurationIsNotTreatedAsAbsent()
    {
        // Arrange
        using var root = new TempDirectory();
        var path = root.File("abox.json", "synthetic-private");
        using var locked = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None
        );

        // Act
        var load = () => FolderConfig.LoadAsync(root.Path, CancellationToken.None);

        // Assert
        var failure = await load.Should().ThrowAsync<AboxException>();
        failure
            .Which.Message.Should()
            .Be("Cannot read abox.json. Check the folder and file permissions.");
    }

    [Theory]
    [InlineData("null", "must contain an object")]
    [InlineData("[]", "must contain an object")]
    [InlineData("{\"squad\":\"ac\",\"squad\":\"bd\"}", "duplicate or unknown")]
    [InlineData("{\"Squad\":\"synthetic-private\"}", "duplicate or unknown")]
    [InlineData("{\"squad\":null}", "nonempty string")]
    [InlineData("{\"squad\":\"  \"}", "nonempty string")]
    [InlineData("{\"userSecrets\":[{\"id\":\"safe\",\"Id\":\"synthetic-private\"}]}", "unique")]
    [InlineData("{\"userSecrets\":[{\"id\":\"safe\",\"id\":\"other\"}]}", "unique")]
    [InlineData("{\"userSecrets\":[{\"keys\":[]}]}", "requires a string 'id'")]
    [InlineData("{\"squad\":\"synthetic-private\", broken}", "not valid JSON")]
    public async Task StrictDecodingRejectsInvalidStructureWithoutEchoingInput(
        string json,
        string diagnostic
    )
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("abox.json", json);

        // Act
        var load = () => FolderConfig.LoadAsync(root.Path, CancellationToken.None);

        // Assert
        var failure = await load.Should().ThrowAsync<AboxException>();
        failure.Which.Message.Should().Contain(diagnostic).And.NotContain("synthetic-private");
    }
}
