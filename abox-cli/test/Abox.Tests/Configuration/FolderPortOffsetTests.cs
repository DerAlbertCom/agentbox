using Abox.Configuration;

namespace Abox.Tests;

public sealed class FolderPortOffsetTests
{
    [Fact]
    public async Task OffsetSnapshotPreservesSquadAndUserSecretSelectionsAfterFileChanges()
    {
        // Arrange
        using var root = new TempDirectory();
        var path = root.File(
            "abox.json",
            """{"squad":"AC","portOffset":-10,"userSecrets":["whole",{"id":"filtered","keys":["Db:*"]}]}"""
        );

        // Act
        var snapshot = await FolderConfig.LoadAsync(root.Path, CancellationToken.None);
        await File.WriteAllTextAsync(path, "{}");

        // Assert
        FolderConfig.SelectPortOffset(snapshot).Should().Be(-10);
        snapshot!.SelectSquad("ac").Should().Be("AC");
        var conflict = () => snapshot.SelectSquad("other");
        conflict.Should().Throw<AboxException>();
        snapshot.UserSecrets.Select(selection => selection.Id).Should().Equal("whole", "filtered");
    }

    [Theory]
    [InlineData("{\"portOffset\":null}")]
    [InlineData("{\"portOffset\":\"10000\"}")]
    [InlineData("{\"portOffset\":10000.0}")]
    [InlineData("{\"portOffset\":1e4}")]
    [InlineData("{\"portOffset\":true}")]
    [InlineData("{\"portOffset\":2147483648}")]
    [InlineData("{\"portOffset\":-2147483648}")]
    [InlineData("{\"portOffset\":65535}")]
    [InlineData("{\"portOffset\":-65535}")]
    [InlineData("{\"portOffset\":0,\"portOffset\":1}")]
    [InlineData("{\"portOffset\":0,\"squad\":null}")]
    [InlineData("{\"portOffset\":0,\"userSecrets\":[\"../unsafe\"]}")]
    [InlineData("{\"portOffset\":0,\"unexpected\":1}")]
    public async Task SharedFolderValidationRejectsInvalidOffsetsAndStillValidatesOtherSelections(
        string json
    )
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("abox.json", json);

        // Act
        var load = () => FolderConfig.LoadAsync(root.Path, CancellationToken.None);

        // Assert
        await load.Should().ThrowAsync<AboxException>();
    }
}
