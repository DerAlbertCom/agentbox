using Abox.Configuration;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsSelectionCommandTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("ac")]
    [InlineData("")]
    [InlineData("other")]
    public async Task OnDemandCopyDoesNotRequireASandboxSquadRecord(string? recordedSquad)
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        var records = new SandboxRecordStore(AboxPaths.For(fixture.Root.Path, fixture.Root.Path));
        if (recordedSquad is not null)
            records.Save(fixture.Name, new SandboxRecord { Squad = recordedSquad });

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(0, fixture.Console.AllText);
        await fixture
            .Sbx.Received(1)
            .WriteUserSecretsAsync(fixture.Name, "sample", "{}", Arg.Any<CancellationToken>());
        (records.Load(fixture.Name)?.Squad).Should().Be(recordedSquad);
        await fixture
            .Sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
        await fixture
            .Sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MalformedUtf8InFolderConfigurationIsRejectedRatherThanNormalized()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        var path = fixture.Root.File("folder/abox.json", "");
        File.WriteAllBytes(path, [123, 34, 115, 113, 117, 97, 100, 34, 58, 34, 0xff, 34, 125]);

        // Act
        var exitCode = await fixture.Run("--force");

        // Assert
        exitCode.Should().Be(1);
        fixture.Console.AllText.Should().Contain("abox.json").And.Contain("UTF");
        await fixture.NoWrites();
    }

    [Fact]
    public async Task AnUnreadableHostStoreIsAnActionableErrorWithoutWrites()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        var source = fixture.Store("sample", "{\"Password\":\"synthetic-private\"}");
        using var locked = new FileStream(
            source,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None
        );

        // Act
        var exitCode = await fixture.Run("--force", "--verbose");

        // Assert
        exitCode.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .Contain("sample")
            .And.Contain("permissions")
            .And.NotContain("synthetic-private");
        await fixture.NoWrites();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{not JSON synthetic-value}")]
    [InlineData("[]")]
    [InlineData("{\"userSecrets\":null}")]
    [InlineData("{\"userSecrets\":[12]}")]
    [InlineData("{\"userSecrets\":[{\"id\":\"sample\",\"keys\":null}]}")]
    [InlineData("{\"userSecrets\":[\"sample\",\"SAMPLE\"]}")]
    [InlineData("{\"userSecrets\":[],\"userSecrets\":[\"sample\"]}")]
    [InlineData("{\"unknown\":\"synthetic-value\"}")]
    [InlineData("{\"squad\":false}")]
    [InlineData("{\"squad\":\"\"}")]
    public async Task MissingOrInvalidFolderConfigurationNeverWidensSelection(string? json)
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        if (json is not null)
            fixture.Root.File("folder/abox.json", json);
        fixture.Store("sample", "{\"Password\":\"synthetic-value\"}");

        // Act
        var exitCode = await fixture.Run("--force");

        // Assert
        exitCode.Should().Be(1);
        fixture.Console.AllText.Should().NotContain("synthetic-value");
        await fixture.NoWrites();
    }

    [Theory]
    [InlineData("{\"Password\":\"synthetic-fragment\", broken}")]
    [InlineData("[\"synthetic-fragment\"]")]
    [InlineData("{\"Password\":\"\\uD800\"}")]
    public async Task ALaterMalformedStoreCausesZeroWritesEvenWithForce(string json)
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["first","later"]}""");
        fixture.Store("first", "{}");
        fixture.Store("later", json);

        // Act
        var exitCode = await fixture.Run("--force", "--verbose");

        // Assert
        exitCode.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .Contain("later")
            .And.NotContain("synthetic-fragment")
            .And.NotContain("D800");
        await fixture.NoWrites();
    }

    [Fact]
    public async Task AMissingOrUnreadableStoreIsActionableAndNeverBypassedByForce()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["missing"]}""");

        // Act
        var exitCode = await fixture.Run("--force");

        // Assert
        exitCode.Should().Be(1);
        fixture.Console.AllText.Should().Contain("missing").And.Contain("permissions");
        await fixture.NoWrites();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppDataHasPrecedenceOnEveryHostAndHomeUsesTheUnixDotNetLayout(bool unix)
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness(unix: unix);
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        fixture.Root.File(
            "home/.microsoft/usersecrets/sample/secrets.json",
            unix ? "{\"Unix\":\"Grüße 🔒\"}" : "malformed unselected fallback"
        );
        if (!unix)
            fixture.Store("sample", "{\"Windows\":\"Grüße 🔒\"}");

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(0);
        await fixture
            .Sbx.Received(1)
            .WriteUserSecretsAsync(
                Arg.Any<string>(),
                "sample",
                Arg.Is<string>(json => HasStringValue(json, unix ? "Unix" : "Windows", "Grüße 🔒")),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("userSecretsDenyList:\n  - '[Stage]'\n", false)]
    [InlineData("userSecretsDenyList:\n  - 'stage.*'\n", true)]
    [InlineData("userSecretsDenyList:\n  - ''\n", false)]
    [InlineData("userSecretsDenyList: invalid\n", false)]
    public async Task GlobalYamlDenyListIsValidatedAndUsesLiteralCaseInsensitiveSubstrings(
        string yaml,
        bool success
    )
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness(configYaml: yaml);
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        fixture.Store("sample", "{\"Endpoint\":\"host[STAGE]server\"}");

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(success ? 0 : 1);
        fixture.Console.AllText.Should().NotContain("host[STAGE]server");
        if (!success)
            await fixture.NoWrites();
    }

    [Theory]
    [InlineData("safe\n")]
    [InlineData("../outside")]
    [InlineData("C:\\outside")]
    [InlineData("/absolute")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("trailing.")]
    [InlineData("CON.txt")]
    public async Task UnsafeIdsAreRejectedBeforeAnyHostStoreOrDestinationAccess(string id)
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File(
            "folder/abox.json",
            System.Text.Json.JsonSerializer.Serialize(new { userSecrets = new[] { id } })
        );

        // Act
        var exitCode = await fixture.Run("--force");

        // Assert
        exitCode.Should().Be(1);
        fixture.Console.AllText.Should().Contain("Unsafe user-secret ID");
        await fixture.NoWrites();
    }

    [Theory]
    [InlineData("{\"A:B\":\"one\",\"a\":{\"b\":\"two\"}}")]
    [InlineData("{\"Password\":\"one\",\"password\":\"two\"}")]
    [InlineData("{\"\":{\"Child\":\"one\"},\":child\":\"two\"}")]
    public async Task DuplicateEffectiveDotNetKeysFailBeforeAnyWriteEvenWithForce(string json)
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["first","later"]}""");
        fixture.Store("first", "{}");
        fixture.Store("later", json);

        // Act
        var exitCode = await fixture.Run("--force");

        // Assert
        exitCode.Should().Be(1);
        fixture
            .Console.AllText.Should()
            .Contain("duplicate")
            .And.NotContain("one")
            .And.NotContain("two");
        await fixture.NoWrites();
    }

    [Fact]
    public async Task InvalidUtf8IsNotSilentlyReplacedInASecretValue()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        var source = fixture.Store("sample", "");
        File.WriteAllBytes(source, [123, 34, 75, 34, 58, 34, 0xff, 34, 125]);

        // Act
        var exitCode = await fixture.Run("--force");

        // Assert
        exitCode.Should().Be(1);
        fixture.Console.AllText.Should().Contain("UTF");
        await fixture.NoWrites();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DenyChecksEveryOriginalValueBeforeAnyDestinationWriteAndForceBypassesOnlyThatPolicy(
        bool force
    )
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness(
            new AboxConfig { UserSecretsDenyList = ["PrOd.Example", "[stage]"] }
        );
        fixture.Configure("""{"squad":"ac","userSecrets":["first","later"]}""");
        fixture.Store("first", """{"Password":"safe-synthetic"}""");
        fixture.Store(
            "later",
            """{"ConnectionStrings":{"Db":"Server=PROD.EXAMPLE;Password=synthetic"}}"""
        );

        // Act
        var exitCode = await fixture.Run(force ? ["--force"] : []);

        // Assert
        exitCode.Should().Be(force ? 0 : 1);
        if (force)
        {
            await fixture
                .Sbx.Received(2)
                .WriteUserSecretsAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                );
            fixture.Console.AllText.Should().Contain("Safety override");
        }
        else
        {
            await fixture
                .Sbx.DidNotReceive()
                .ValidateUserSecretsDestinationAsync(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<CancellationToken>()
                );
            await fixture
                .Sbx.DidNotReceive()
                .WriteUserSecretsAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                );
            fixture
                .Console.AllText.Should()
                .Contain("later")
                .And.Contain("ConnectionStrings:Db")
                .And.Contain("--force");
        }
        fixture
            .Console.AllText.Should()
            .NotContain("PROD.EXAMPLE")
            .And.NotContain("Password=synthetic");
    }

    [Fact]
    public async Task CopiesOnlyNamedStoresIntoTheExistingFoldersSandbox()
    {
        // Arrange
        using var fixture = new UserSecretsCliHarness();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        var source = fixture.Store(
            "sample",
            """{"Nested":{"Password":"synthetic-only"},"Enabled":true}"""
        );
        fixture.Store("unselected", "not JSON");

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(0);
        await fixture
            .Sbx.Received(1)
            .WriteUserSecretsAsync(
                SandboxName.For(fixture.Location),
                "sample",
                Arg.Is<string>(json => json.Contains("synthetic-only") && json.Contains("Nested")),
                Arg.Any<CancellationToken>()
            );
        File.ReadAllText(source)
            .Should()
            .Be("""{"Nested":{"Password":"synthetic-only"},"Enabled":true}""");
        fixture
            .Console.AllText.Should()
            .Contain("sample")
            .And.Contain("2 keys")
            .And.NotContain("synthetic-only");
        await fixture
            .Sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
    }

    private static bool HasStringValue(string json, string key, string value)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        return document.RootElement.GetProperty(key).GetString() == value;
    }
}
