using Abox.Configuration;
using Abox.Naming;
using Abox.Sbx;
using NSubstitute;
using Fixture = Abox.Tests.UserSecretsCliHarness;

namespace Abox.Tests;

public sealed class UserSecretsCommandTests
{
    [Fact]
    public async Task MalformedUtf8InFolderConfigurationIsRejectedRatherThanNormalized()
    {
        using var fixture = new Fixture();
        var path = fixture.Root.File("folder/abox.json", "");
        File.WriteAllBytes(path, [123, 34, 115, 113, 117, 97, 100, 34, 58, 34, 0xff, 34, 125]);

        (await fixture.Run("--force")).Should().Be(1);

        fixture.Console.AllText.Should().Contain("abox.json").And.Contain("UTF");
        await fixture.NoWrites();
    }

    [Fact]
    public async Task ConfiguredDefaultLocationIsUsedWhenLocationIsOmitted()
    {
        using var fixture = new Fixture();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");

        (await fixture.RunDefault()).Should().Be(0);

        await fixture
            .Sbx.Received(1)
            .FindSandboxAsync(SandboxName.For(fixture.Location), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AnUnreadableHostStoreIsAnActionableErrorWithoutWrites()
    {
        using var fixture = new Fixture();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        var source = fixture.Store("sample", "{\"Password\":\"synthetic-private\"}");
        using var locked = new FileStream(
            source,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None
        );

        (await fixture.Run("--force", "--verbose")).Should().Be(1);

        fixture
            .Console.AllText.Should()
            .Contain("sample")
            .And.Contain("permissions")
            .And.NotContain("synthetic-private");
        await fixture.NoWrites();
    }

    [Fact]
    public async Task PreflightFailureDoesNotReachTheDestination()
    {
        using var fixture = new Fixture();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture.Sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 50, 0));

        (await fixture.Run()).Should().Be(1);

        await fixture.NoWrites();
        await fixture
            .Sbx.DidNotReceive()
            .FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancelledCopyUsesExit130AndExplainsSafeRetryWithoutLeakingCancellationDetails()
    {
        using var fixture = new Fixture();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromException(
                    new OperationCanceledException("synthetic-password 123456789 true")
                )
            );

        (await fixture.Run("--verbose")).Should().Be(130);

        fixture
            .Console.AllText.Should()
            .Contain("retry")
            .And.Contain("rollback")
            .And.NotContain("synthetic-password")
            .And.NotContain("123456789 true");
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
    public async Task MissingOrInvalidFolderConfigurationNeverWidensSelection(string? json)
    {
        using var fixture = new Fixture();
        if (json is not null)
            fixture.Root.File("folder/abox.json", json);
        fixture.Store("sample", "{\"Password\":\"synthetic-value\"}");

        (await fixture.Run("--force")).Should().Be(1);

        fixture.Console.AllText.Should().NotContain("synthetic-value");
        await fixture.NoWrites();
    }

    [Theory]
    [InlineData("{\"Password\":\"synthetic-fragment\", broken}")]
    [InlineData("[\"synthetic-fragment\"]")]
    [InlineData("{\"Password\":\"\\uD800\"}")]
    public async Task ALaterMalformedStoreCausesZeroWritesEvenWithForce(string json)
    {
        using var fixture = new Fixture();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["first","later"]}""");
        fixture.Store("first", "{}");
        fixture.Store("later", json);

        (await fixture.Run("--force", "--verbose")).Should().Be(1);

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
        using var fixture = new Fixture();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["missing"]}""");
        (await fixture.Run("--force")).Should().Be(1);
        fixture.Console.AllText.Should().Contain("missing").And.Contain("permissions");
        await fixture.NoWrites();
    }

    [Fact]
    public async Task DoesNotCreateOrReplaceAMissingSandbox()
    {
        using var fixture = new Fixture();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((SandboxInfo?)null);

        (await fixture.Run()).Should().Be(1);

        fixture.Console.AllText.Should().Contain("abox create");
        await fixture.NoWrites();
        await fixture
            .Sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
        await fixture
            .Sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransferAndTargetPreparationFailuresAreValueFreeAndForceDoesNotBypassThem(
        bool targetFailure
    )
    {
        using var fixture = new Fixture();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["first","later"]}""");
        fixture.Store("first", "{}");
        fixture.Store("later", "{}");
        if (targetFailure)
            fixture
                .Sbx.ValidateUserSecretsDestinationAsync(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(
                    Task.FromException(new AboxException("synthetic-password 123456789 true"))
                );
        else
            fixture
                .Sbx.WriteUserSecretsAsync(
                    Arg.Any<string>(),
                    "later",
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(
                    Task.FromException(new AboxException("synthetic-password 123456789 true"))
                );

        (await fixture.Run("--force", "--verbose")).Should().Be(1);

        fixture
            .Console.AllText.Should()
            .NotContain("synthetic-password")
            .And.NotContain("123456789 true")
            .And.Contain("retry");
        if (targetFailure)
            await fixture.NoWrites();
        else
            fixture.Console.AllText.Should().Contain("1 completed").And.Contain("rollback");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AppDataHasPrecedenceOnEveryHostAndHomeUsesTheUnixDotNetLayout(bool unix)
    {
        using var fixture = new Fixture(unix: unix);
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        fixture.Root.File(
            "home/.microsoft/usersecrets/sample/secrets.json",
            unix ? "{\"Unix\":\"Grüße 🔒\"}" : "malformed unselected fallback"
        );
        if (!unix)
            fixture.Store("sample", "{\"Windows\":\"Grüße 🔒\"}");

        (await fixture.Run()).Should().Be(0);

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
        using var fixture = new Fixture(configYaml: yaml);
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        fixture.Store("sample", "{\"Endpoint\":\"host[STAGE]server\"}");

        (await fixture.Run()).Should().Be(success ? 0 : 1);

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
        using var fixture = new Fixture();
        fixture.Root.File(
            "folder/abox.json",
            System.Text.Json.JsonSerializer.Serialize(new { userSecrets = new[] { id } })
        );

        (await fixture.Run("--force")).Should().Be(1);

        fixture.Console.AllText.Should().Contain("Unsafe user-secret ID");
        await fixture.NoWrites();
    }

    [Theory]
    [InlineData("{\"A:B\":\"one\",\"a\":{\"b\":\"two\"}}")]
    [InlineData("{\"Password\":\"one\",\"password\":\"two\"}")]
    [InlineData("{\"\":{\"Child\":\"one\"},\":child\":\"two\"}")]
    public async Task DuplicateEffectiveDotNetKeysFailBeforeAnyWriteEvenWithForce(string json)
    {
        using var fixture = new Fixture();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["first","later"]}""");
        fixture.Store("first", "{}");
        fixture.Store("later", json);

        (await fixture.Run("--force")).Should().Be(1);

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
        using var fixture = new Fixture();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        var source = fixture.Store("sample", "");
        File.WriteAllBytes(source, [123, 34, 75, 34, 58, 34, 0xff, 34, 125]);

        (await fixture.Run("--force")).Should().Be(1);

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
        using var fixture = new Fixture(
            new AboxConfig { UserSecretsDenyList = ["PrOd.Example", "[stage]"] }
        );
        fixture.Root.File("folder/abox.json", """{"userSecrets":["first","later"]}""");
        fixture.Store("first", """{"Password":"safe-synthetic"}""");
        fixture.Store(
            "later",
            """{"ConnectionStrings":{"Db":"Server=PROD.EXAMPLE;Password=synthetic"}}"""
        );

        (await fixture.Run(force ? ["--force"] : [])).Should().Be(force ? 0 : 1);

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
        using var fixture = new Fixture();
        fixture.Root.File("folder/abox.json", """{"userSecrets":["sample"]}""");
        var source = fixture.Store(
            "sample",
            """{"Nested":{"Password":"synthetic-only"},"Enabled":true}"""
        );
        fixture.Store("unselected", "not JSON");

        (await fixture.Run()).Should().Be(0);

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
