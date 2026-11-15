using System.Text.Json;
using Abox.Configuration;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsFilteringTests
{
    [Fact]
    public async Task MixedEntriesCopyWholeStoresAndExactSelectedKeysWithoutChangingHostFiles()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Configure(
            """["whole", {"id":"object-whole"}, {"id":"filtered","keys":["Enabled"]}]"""
        );
        fixture.Store("whole", """{"Password":"synthetic-whole","Other":12}""");
        fixture.Store("object-whole", """{"Token":"synthetic-object"}""");
        var source = fixture.Store(
            "filtered",
            """{"Enabled":true,"Omitted":"synthetic-unselected"}"""
        );
        var original = File.ReadAllBytes(source);
        fixture.Store("unlisted", "malformed and never read");

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(0);
        fixture
            .Writes.Select(write => write.Id)
            .Should()
            .Equal("whole", "object-whole", "filtered");
        fixture.Payload("whole").Should().Be("""{"Password":"synthetic-whole","Other":12}""");
        fixture.Payload("object-whole").Should().Be("""{"Token":"synthetic-object"}""");
        fixture.Payload("filtered").Should().Be("""{"Enabled":true}""");
        File.ReadAllBytes(source).Should().Equal(original);
        fixture
            .Console.AllText.Should()
            .Contain("2 keys")
            .And.Contain("1 keys")
            .And.NotContain("synthetic-");
    }

    [Fact]
    public async Task PatternsMatchEffectiveKeysIgnoringCaseAcrossSegmentsWithoutDuplicatingOverlaps()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Configure(
            """[{"id":"sample","keys":["connectionstrings:*","ConnectionStrings:Db","Keycloak:*","Services:2:Token"]}]"""
        );
        fixture.Store(
            "sample",
            """
            {
              "ConnectionStrings": {"Db":"synthetic-db","Nested":{"Readonly":"synthetic-reader"}},
              "Keycloak:Authority":"synthetic-authority",
              "Services":[{"Token":"synthetic-omit-0"},{"Token":"synthetic-omit-1"},{"Token":"synthetic-keep-2"}],
              "Unmatched":"synthetic-omit"
            }
            """
        );

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(0);
        using var payload = JsonDocument.Parse(fixture.Payload("sample"));
        using var expected = JsonDocument.Parse(
            """
            {"ConnectionStrings:Db":"synthetic-db","ConnectionStrings:Nested:Readonly":"synthetic-reader",
             "Keycloak:Authority":"synthetic-authority","Services:2:Token":"synthetic-keep-2"}
            """
        );
        JsonElement.DeepEquals(payload.RootElement, expected.RootElement).Should().BeTrue();
        fixture.Console.AllText.Should().Contain("4 keys").And.NotContain("synthetic-");

        // Arrange
        var first = fixture.Payload("sample");
        fixture.Configure(
            """[{"id":"sample","keys":["Services:2:Token","KEYCLOAK:*","ConnectionStrings:Db","connectionstrings:*","CONNECTIONSTRINGS:DB"]}]"""
        );
        // Act
        var reorderedExitCode = await fixture.Run();

        // Assert
        reorderedExitCode.Should().Be(0);
        fixture.Payload("sample").Should().Be(first);
    }

    [Theory]
    [InlineData("[{\"id\":\"sample\",\"keys\":[\"\"]}]")]
    [InlineData("[{\"id\":\"sample\",\"keys\":[\"  \"]}]")]
    [InlineData("[{\"id\":\"sample\",\"keys\":[\"synthetic-private\\n\"]}]")]
    [InlineData("[{\"id\":\"sample\",\"keys\":[null]}]")]
    [InlineData("[{\"id\":\"sample\",\"keys\":[12]}]")]
    [InlineData("[{\"id\":\"sample\",\"keys\":[{}]}]")]
    [InlineData("[{\"id\":\"sample\",\"keys\":null}]")]
    [InlineData("[{\"id\":\"sample\",\"keys\":\"*\"}]")]
    [InlineData("[{\"keys\":[\"*\"]}]")]
    [InlineData("[{\"id\":null}]")]
    [InlineData("[{\"id\":12}]")]
    [InlineData("[{\"id\":\"../escape\"}]")]
    [InlineData("[{\"id\":\"sample\",\"id\":\"other\"}]")]
    [InlineData("[{\"id\":\"sample\",\"keys\":[],\"keys\":[\"*\"]}]")]
    [InlineData("[{\"id\":\"sample\",\"unknown\":\"synthetic-private\"}]")]
    [InlineData("[\"sample\",{\"id\":\"SAMPLE\",\"keys\":[]}]")]
    public async Task MalformedSelectionsFailClosedEvenWithForceAndVerbose(string entries)
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Configure(entries);
        fixture.Store("sample", """{"Password":"synthetic-private"}""");

        // Act
        var exitCode = await fixture.Run("--force", "--verbose");

        // Assert
        exitCode.Should().Be(1);
        fixture.Writes.Should().BeEmpty();
        fixture.Sbx.ReceivedCalls().Should().BeEmpty();
        fixture.Console.AllText.Should().NotBeNullOrWhiteSpace();
        fixture.Console.AllText.Should().NotContain("synthetic-private");
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("[\"NoSuchKey:*\"]")]
    public async Task RerunsNarrowTheReplacementAndAnEmptySelectionWritesAnEmptyObject(
        string emptyPatterns
    )
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Store("sample", """{"Keep":"synthetic-keep","Remove":"synthetic-remove"}""");
        fixture.Configure("""["sample"]""");
        // Act
        var wholeStoreExitCode = await fixture.Run();

        // Assert
        wholeStoreExitCode.Should().Be(0);
        fixture.Payload("sample").Should().Contain("Remove");

        // Arrange
        fixture.Configure("""[{"id":"sample","keys":["Keep"]}]""");

        // Act
        var narrowedExitCode = await fixture.Run();

        // Assert
        narrowedExitCode.Should().Be(0);
        fixture.Payload("sample").Should().Be("""{"Keep":"synthetic-keep"}""");

        // Arrange
        fixture.Configure("[{\"id\":\"sample\",\"keys\":" + emptyPatterns + "}]");

        // Act
        var emptySelectionExitCode = await fixture.Run("--verbose");

        // Assert
        emptySelectionExitCode.Should().Be(0);
        fixture.Writes.Should().HaveCount(3);
        fixture.Payload("sample").Should().Be("{}");
        fixture.Console.AllText.Should().Contain("sample': 0 keys").And.NotContain("synthetic-");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DenyChecksOnlySelectedOriginalValuesAcrossAllStoresBeforeAnyWrite(bool force)
    {
        // Arrange
        using var fixture = new Fixture(new AboxConfig { UserSecretsDenyList = ["PrOd.Example"] });
        fixture.Configure("""[{"id":"first","keys":["Safe"]},{"id":"later","keys":["Safe"]}]""");
        fixture.Store(
            "first",
            """{"Safe":"synthetic-local","Ignored":"https://PROD.EXAMPLE/unmatched"}"""
        );
        fixture.Store(
            "later",
            """{"Safe":"synthetic-local","Ignored":"https://PROD.EXAMPLE/unmatched"}"""
        );

        // Act
        var safeExitCode = await fixture.Run();

        // Assert
        safeExitCode.Should().Be(0);
        fixture.Writes.Should().HaveCount(2);
        fixture.Payload("first").Should().Be("""{"Safe":"synthetic-local"}""");
        fixture.Payload("later").Should().Be("""{"Safe":"synthetic-local"}""");

        // Arrange
        fixture.Writes.Clear();
        fixture.Sbx.ClearReceivedCalls();
        fixture.Store("later", """{"Safe":"https://PROD.EXAMPLE/synthetic-selected"}""");

        // Act
        var deniedExitCode = await fixture.Run(force ? ["--force", "--verbose"] : ["--verbose"]);

        // Assert
        deniedExitCode.Should().Be(force ? 0 : 1);
        if (force)
        {
            fixture.Writes.Should().HaveCount(2);
            fixture
                .Payload("later")
                .Should()
                .Be("""{"Safe":"https://PROD.EXAMPLE/synthetic-selected"}""");
            fixture.Console.AllText.Should().Contain("Safety override");
        }
        else
        {
            fixture.Writes.Should().BeEmpty();
            fixture.Sbx.ReceivedCalls().Should().BeEmpty();
            fixture
                .Console.AllText.Should()
                .Contain("later")
                .And.Contain("Safe")
                .And.Contain("--force");
        }
        fixture.Console.AllText.Should().NotContain("PROD.EXAMPLE").And.NotContain("synthetic-");
    }

    [Theory]
    [InlineData("{\"Ignored\":\"synthetic-private\",\"ignored\":\"synthetic-other\"}")]
    [InlineData(
        "{\"Ignored:Child\":\"synthetic-private\",\"ignored\":{\"Child\":\"synthetic-other\"}}"
    )]
    [InlineData("{\"Ignored\":[\"synthetic-private\"],\"ignored:0\":\"synthetic-other\"}")]
    [InlineData("{\"Ignored\":\"synthetic-private\", broken}")]
    [InlineData("{\"Ignored\":\"\\uD800\"}")]
    public async Task FilteringAndForceDoNotBypassInvalidInputEvenWhenNoKeysMatch(string json)
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Configure("""["first",{"id":"later","keys":[]}]""");
        fixture.Store("first", "{}");
        fixture.Store("later", json);

        // Act
        var exitCode = await fixture.Run("--force", "--verbose");

        // Assert
        exitCode.Should().Be(1);
        fixture.Writes.Should().BeEmpty();
        fixture.Sbx.ReceivedCalls().Should().BeEmpty();
        fixture.Console.AllText.Should().Contain("later").And.NotContain("synthetic-");
    }

    [Theory]
    [InlineData("*", true)]
    [InlineData("**", true)]
    [InlineData("Conn*Db", true)]
    [InlineData("*nection*:*D*", true)]
    [InlineData("connectionstrings:db", true)]
    [InlineData("ConnectionStrings:*", true)]
    [InlineData("*ConnectionStrings:Db*", true)]
    [InlineData("ConnectionStrings:Db*Db", false)]
    [InlineData("ConnectionStrings:", false)]
    [InlineData("ConnectionStrings", false)]
    [InlineData("Strings:*", false)]
    [InlineData("ConnectionStrings:D", false)]
    [InlineData("ConnectionStrings:?b", false)]
    [InlineData("ConnectionStrings:[D]b", false)]
    public async Task OnlyStarIsAWildcardAndMatchingCoversTheEntireEffectiveKey(
        string pattern,
        bool matches
    )
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Configure(
            JsonSerializer.Serialize(new[] { new { id = "sample", keys = new[] { pattern } } })
        );
        fixture.Store("sample", """{"ConnectionStrings:Db":"synthetic-private"}""");

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(0);
        fixture
            .Payload("sample")
            .Should()
            .Be(matches ? """{"ConnectionStrings:Db":"synthetic-private"}""" : "{}");
        fixture.Console.AllText.Should().NotContain("synthetic-private");
    }

    [Fact]
    public async Task NonWildcardPatternCharactersAreLiteralAndSelectedJsonScalarTypesArePreserved()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Configure("""[{"id":"sample","keys":["Literal:?[]\\.Key","Nested:*"]}]""");
        fixture.Store(
            "sample",
            """
            {"Literal:?[]\\.Key":"Grüße 🔒", "Literal:D.Key":"synthetic-omit",
             "Nested":{"Number":12.50,"Boolean":false,"Null":null,"EmptyObject":{},"EmptyArray":[]}}
            """
        );

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(0);
        using var payload = JsonDocument.Parse(fixture.Payload("sample"));
        using var expected = JsonDocument.Parse(
            """
            {"Literal:?[]\\.Key":"Grüße 🔒","Nested:Number":12.50,"Nested:Boolean":false,
             "Nested:Null":null,"Nested:EmptyObject":{},"Nested:EmptyArray":[]}
            """
        );
        JsonElement.DeepEquals(payload.RootElement, expected.RootElement).Should().BeTrue();
        fixture
            .Console.AllText.Should()
            .Contain("6 keys")
            .And.NotContain("Grüße")
            .And.NotContain("synthetic-");
    }

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

    [Fact]
    public async Task FilteredPayloadDoesNotRetainCommentsEvenInsideSelectedEmptyContainers()
    {
        // Arrange
        using var fixture = new Fixture(new AboxConfig { UserSecretsDenyList = ["prod.example"] });
        fixture.Configure("""[{"id":"sample","keys":["Keep:*"]}]""");
        fixture.Store(
            "sample",
            """
            {/* synthetic-private PROD.EXAMPLE */
             "Keep":{"Object":{/* synthetic-private PROD.EXAMPLE */},
                     "Array":[/* synthetic-private PROD.EXAMPLE */],
                     "String":/* synthetic-private PROD.EXAMPLE */"synthetic-local"},
             "Ignored":"synthetic-private PROD.EXAMPLE"}
            """
        );

        // Act
        var exitCode = await fixture.Run("--verbose");

        // Assert
        exitCode.Should().Be(0);
        fixture
            .Payload("sample")
            .Should()
            .Be("""{"Keep:Object":{},"Keep:Array":[],"Keep:String":"synthetic-local"}""");
        fixture.Console.AllText.Should().NotContain("synthetic-").And.NotContain("PROD.EXAMPLE");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly UserSecretsCliHarness cli;
        public TempDirectory Root => cli.Root;
        public TestConsole Console => cli.Console;
        public ISbxClient Sbx => cli.Sbx;
        public List<(string Id, string Json)> Writes { get; } = [];

        public Fixture(AboxConfig? config = null)
        {
            cli = new UserSecretsCliHarness(config);
            Sbx.WriteUserSecretsAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(call =>
                {
                    Writes.Add((call.ArgAt<string>(1), call.ArgAt<string>(2)));
                    return Task.CompletedTask;
                });
        }

        public void Configure(string entries) =>
            Root.File("folder/abox.json", "{\"userSecrets\":" + entries + "}");

        public string Store(string id, string json) => cli.Store(id, json);

        public string Payload(string id) => Writes.Last(write => write.Id == id).Json;

        public Task<int> Run(params string[] args) => cli.Run(args);

        public void Dispose() => cli.Dispose();
    }
}
