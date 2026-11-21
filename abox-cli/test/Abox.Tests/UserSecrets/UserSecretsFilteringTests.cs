using Abox.Configuration;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsFilteringTests
{
    [Fact]
    public async Task TheSharedFlowKeepsOnlySelectedOriginalValuesInItsOpaqueSnapshot()
    {
        // Arrange
        using var root = new TempDirectory();
        root.File(
            "folder/abox.json",
            """{"userSecrets":[{"id":"sample","keys":["Nested:Keep"]}]}"""
        );
        var source = root.File(
            "home/.microsoft/usersecrets/sample/secrets.json",
            """
            {"Nested":{"Keep":"synthetic-original","Ignored":"https://PROD.EXAMPLE/unmatched"}}
            """
        );
        var sbx = Substitute.For<ISbxClient>();
        var copy = new UserSecretsCopy(
            new AboxConfig { UserSecretsDenyList = ["prod.example"] },
            new UserSecretsHost(null, Path.Combine(root.Path, "home")),
            sbx
        );

        // Act
        var prepared = await copy.PrepareAsync(
            await FolderConfig.LoadAsync(Path.Combine(root.Path, "folder"), CancellationToken.None),
            false,
            CancellationToken.None
        );
        // Assert
        sbx.ReceivedCalls().Should().BeEmpty();

        // Arrange
        File.WriteAllText(source, "malformed changed after validation");

        // Act
        var report = await copy.CopyAsync(prepared, "sandbox", CancellationToken.None);

        // Assert
        await sbx.Received(1)
            .WriteUserSecretsAsync(
                "sandbox",
                "sample",
                """{"Nested:Keep":"synthetic-original"}""",
                Arg.Any<CancellationToken>()
            );
        report.Stores.Should().ContainSingle().Which.KeyCount.Should().Be(1);
        report.ToString().Should().NotContain("synthetic-");
        prepared.ToString().Should().NotContain("synthetic-").And.NotContain("PROD.EXAMPLE");
    }
}
