using System.Text.Json;
using Abox.Configuration;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsSafetyCommandTests
{
    [Theory]
    [InlineData("success", 0)]
    [InlineData("preflight", 1)]
    [InlineData("lookup", 1)]
    [InlineData("destination", 1)]
    [InlineData("transfer", 1)]
    [InlineData("cancellation", 130)]
    public async Task AnActualOverrideIsReportedOnlyAfterSuccessfulCopy(string result, int exitCode)
    {
        // Arrange
        using var fixture = new UserSecretsSafetyCommandFixture();
        fixture.Store("""{"Db":"Server=PROD.EXAMPLE;Password=synthetic"}""");
        if (result == "preflight")
            fixture
                .Sbx.GetVersionAsync(Arg.Any<CancellationToken>())
                .Returns(new Version(0, 50, 0));
        if (result == "lookup")
            fixture
                .Sbx.FindSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((SandboxInfo?)null);
        if (result == "destination")
            fixture
                .Sbx.ValidateUserSecretsDestinationAsync(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(Task.FromException(new AboxException("synthetic failure")));
        var overrideReportedDuringCopy = false;
        fixture
            .Sbx.WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                overrideReportedDuringCopy = fixture.Console.AllText.Contains("Safety override");
                return result switch
                {
                    "transfer" => Task.FromException(new AboxException("synthetic failure")),
                    "cancellation" => Task.FromException(new OperationCanceledException()),
                    _ => Task.CompletedTask,
                };
            });

        // Act
        var actualExitCode = await fixture.Run("--force");

        // Assert
        actualExitCode.Should().Be(exitCode);
        overrideReportedDuringCopy.Should().BeFalse();
        if (result == "success")
            fixture.Console.AllText.Should().Contain("Safety override").And.Contain("matched");
        else
            fixture.Console.AllText.Should().NotContain("Safety override");
        fixture
            .Console.AllText.Should()
            .NotContain("PROD.EXAMPLE")
            .And.NotContain("Password=synthetic");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ForceWithoutADenyMatchDoesNotClaimThatAnOverrideWasUsed(bool emptySelection)
    {
        // Arrange
        using var fixture = new UserSecretsSafetyCommandFixture();
        if (emptySelection)
            fixture.Root.File("folder/abox.json", """{"userSecrets":[]}""");
        else
            fixture.Store("""{"Db":"Server=localhost"}""");

        // Act
        var exitCode = await fixture.Run("--force");

        // Assert
        exitCode.Should().Be(0);
        fixture.Console.AllText.Should().NotContain("Safety override");
    }

    [Theory]
    [InlineData(false, "{ /* Server=prod.example;Password=secret */ \"Db\":\"Server=localhost\" }")]
    [InlineData(true, "{ /* Server=prod.example;Password=secret */ \"Db\":\"Server=localhost\" }")]
    [InlineData(
        false,
        "{\"Nested\":{/* prod.example */\"Items\":[\"Server=localhost\",/* prod.example */],},// prod.example\n}"
    )]
    [InlineData(
        true,
        "{\"Nested\":{/* prod.example */\"Items\":[\"Server=localhost\",/* prod.example */],},// prod.example\n}"
    )]
    public async Task OnlyParsedJsonDataIsTransferredNeverCommentsOrTrailingCommas(
        bool filtered,
        string source
    )
    {
        // Arrange
        using var fixture = new UserSecretsSafetyCommandFixture();
        if (filtered)
            fixture.Root.File(
                "folder/abox.json",
                """{"userSecrets":[{"id":"sample","keys":["Db","Nested:*"]}]}"""
            );
        fixture.Store(source);
        string? transferred = null;
        fixture
            .Sbx.WriteUserSecretsAsync(
                Arg.Any<string>(),
                "sample",
                Arg.Do<string>(json => transferred = json),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.CompletedTask);

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(0);
        transferred.Should().NotBeNull();
        transferred.Should().NotContain("prod.example").And.NotContain("/*").And.NotContain("//");
        using var document = JsonDocument.Parse(transferred!);
        document.RootElement.ValueKind.Should().Be(JsonValueKind.Object);
        transferred.Should().Contain("Server=localhost");
        File.ReadAllText(fixture.SourcePath).Should().Be(source);
    }

    private sealed class UserSecretsSafetyCommandFixture : IDisposable
    {
        private readonly UserSecretsCliHarness cli = new(
            new AboxConfig { UserSecretsDenyList = ["prod.example"] }
        );
        public TempDirectory Root => cli.Root;
        public TestConsole Console => cli.Console;
        public ISbxClient Sbx => cli.Sbx;
        public string SourcePath =>
            Path.Combine(Root.Path, "appdata/Microsoft/UserSecrets/sample/secrets.json");

        public UserSecretsSafetyCommandFixture()
        {
            cli.Configure("""{"userSecrets":["sample"]}""");
        }

        public void Store(string json) => cli.Store("sample", json);

        public Task<int> Run(params string[] args) => cli.Run(args);

        public void Dispose() => cli.Dispose();
    }
}
