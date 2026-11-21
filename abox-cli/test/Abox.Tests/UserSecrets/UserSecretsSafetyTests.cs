using System.Text.Json;
using Abox.Configuration;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsSafetyTests
{
    [Theory]
    [InlineData(false, "Server=localhost", false)]
    [InlineData(true, "Server=localhost", false)]
    [InlineData(true, "Server=PROD.EXAMPLE", true)]
    public async Task SharedReportsRecordAnActualSelectedOriginalValueOverride(
        bool force,
        string value,
        bool expectedOverride
    )
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("abox.json", """{"userSecrets":["sample"]}""");
        root.File(
            ".microsoft/usersecrets/sample/secrets.json",
            JsonSerializer.Serialize(new { Db = value })
        );
        var copy = new UserSecretsCopy(
            new AboxConfig { UserSecretsDenyList = ["prod.example"] },
            new UserSecretsHost(null, root.Path),
            Substitute.For<ISbxClient>()
        );

        // Act
        var prepared = await copy.PrepareAsync(
            await FolderConfig.LoadAsync(root.Path, CancellationToken.None),
            force,
            CancellationToken.None
        );
        var report = await copy.CopyAsync(prepared, "sandbox", CancellationToken.None);

        // Assert
        prepared.SafetyOverride.Should().Be(expectedOverride);
        report.SafetyOverride.Should().Be(expectedOverride);
    }
}
