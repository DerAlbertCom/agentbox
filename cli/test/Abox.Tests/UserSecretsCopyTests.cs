using Abox.Configuration;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsCopyTests
{
    [Fact]
    public async Task PreparationIsSideEffectFreeAndCopyUsesTheOriginalInMemorySnapshot()
    {
        using var root = new TempDirectory();
        root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        var source = root.File(
            "home/.microsoft/usersecrets/sample/secrets.json",
            """{"Nested":{"Password":"synthetic-original"}}"""
        );
        var sbx = Substitute.For<ISbxClient>();
        var copy = new UserSecretsCopy(
            new AboxConfig(),
            new UserSecretsHost(null, Path.Combine(root.Path, "home")),
            sbx
        );

        var prepared = await copy.PrepareAsync(
            await FolderConfig.LoadAsync(Path.Combine(root.Path, "folder"), CancellationToken.None),
            false,
            CancellationToken.None
        );
        sbx.ReceivedCalls().Should().BeEmpty();
        File.WriteAllText(source, "malformed changed after validation");
        var report = await copy.CopyAsync(prepared, "sandbox", CancellationToken.None);

        await sbx.Received(1)
            .WriteUserSecretsAsync(
                "sandbox",
                "sample",
                Arg.Is<string>(json => json.Contains("synthetic-original")),
                Arg.Any<CancellationToken>()
            );
        report.Stores.Should().ContainSingle().Which.KeyCount.Should().Be(1);
        prepared.ToString().Should().NotContain("synthetic-original");
        report.ToString().Should().NotContain("synthetic-original");
    }

    [Fact]
    public async Task MissingFolderConfigurationIsANoopEvenWithoutUsableHostEnvironment()
    {
        var sbx = Substitute.For<ISbxClient>();
        var copy = new UserSecretsCopy(
            new AboxConfig { UserSecretsDenyList = [""] },
            new UserSecretsHost(null, null),
            sbx
        );

        var prepared = await copy.PrepareAsync(null, false, CancellationToken.None);
        var report = await copy.CopyAsync(prepared, "sandbox", CancellationToken.None);

        report.Stores.Should().BeEmpty();
        sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DirectlyConstructedNullDenyPolicyIsRejectedEvenWithForce(bool force)
    {
        using var root = new TempDirectory();
        root.File("abox.json", """{"userSecrets":["missing"]}""");
        var sbx = Substitute.For<ISbxClient>();
        var copy = new UserSecretsCopy(
            new AboxConfig { UserSecretsDenyList = null! },
            new UserSecretsHost(null, null),
            sbx
        );
        var folder = await FolderConfig.LoadAsync(root.Path, CancellationToken.None);

        var action = () => copy.PrepareAsync(folder, force, CancellationToken.None);

        await action.Should().ThrowAsync<AboxException>().WithMessage("*userSecretsDenyList*");
        sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    public async Task InvalidAppDataDoesNotFallBackToAnOtherwiseUsableHome(string appData)
    {
        using var root = new TempDirectory();
        root.File("abox.json", """{"userSecrets":["sample"]}""");
        root.File(".microsoft/usersecrets/sample/secrets.json", "{}");
        var sbx = Substitute.For<ISbxClient>();
        var copy = new UserSecretsCopy(
            new AboxConfig(),
            new UserSecretsHost(appData, root.Path),
            sbx
        );

        var action = async () =>
            await copy.PrepareAsync(
                await FolderConfig.LoadAsync(root.Path, CancellationToken.None),
                true,
                CancellationToken.None
            );

        await action.Should().ThrowAsync<AboxException>().WithMessage("*APPDATA*HOME*");
        sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task CancellationIsPreservedByBothPhases()
    {
        using var root = new TempDirectory();
        root.File("abox.json", """{"userSecrets":["sample"]}""");
        root.File(".microsoft/usersecrets/sample/secrets.json", "{}");
        var sbx = Substitute.For<ISbxClient>();
        var copy = new UserSecretsCopy(new AboxConfig(), new UserSecretsHost(null, root.Path), sbx);
        var folder = await FolderConfig.LoadAsync(root.Path, CancellationToken.None);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var preparation = () => copy.PrepareAsync(folder, false, cancelled.Token);
        await preparation.Should().ThrowAsync<OperationCanceledException>();
        var prepared = await copy.PrepareAsync(folder, false, CancellationToken.None);
        var copying = () => copy.CopyAsync(prepared, "sandbox", cancelled.Token);
        await copying.Should().ThrowAsync<OperationCanceledException>();
        await sbx.DidNotReceive()
            .WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }
}
