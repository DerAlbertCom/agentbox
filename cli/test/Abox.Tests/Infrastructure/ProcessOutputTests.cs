using Abox.Infrastructure;

namespace Abox.Tests;

public sealed class ProcessOutputTests
{
    [Fact]
    public void DiagnosticsDoNotIncludeArgumentsEnvironmentOrStandardInput()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ProcessOutput(console) { Verbose = true };
        var request = new ProcessRequest(
            "sbx",
            ["secret", "set", "private-service", "--token", "private-argument"],
            Environment: new Dictionary<string, string>
            {
                ["PRIVATE_SETTING"] = "private-environment",
            },
            StandardInput: "private-input"
        );

        // Act
        output.Write(request, new ProcessResult(0, "stored", "warning"));

        // Assert
        console.AllText.Should().Contain("sbx").And.Contain("stored").And.Contain("warning");
        console.AllText.Should().NotContain("private-").And.NotContain("PRIVATE_SETTING");
        console.Output.Should().BeEmpty();
    }

    [Fact]
    public void KnownCredentialsEchoedByAToolAreRedactedWithoutChangingCapturedOutput()
    {
        // Arrange
        var console = new TestConsole();
        var output = new ProcessOutput(console) { Verbose = true };
        var request = new ProcessRequest(
            "sbx",
            ["--password", "argument-password", "--api-key=argument-api-key"],
            Environment: new Dictionary<string, string> { ["API_TOKEN"] = "environment-token" },
            StandardInput: "stdin-secret"
        );
        const string text = "stdin-secret environment-token argument-password argument-api-key";
        var result = new ProcessResult(7, text, text);

        // Act
        output.Write(request, result);

        // Assert
        console.AllText.Should().NotContain("stdin-secret").And.NotContain("environment-token");
        console.AllText.Should().NotContain("argument-password").And.NotContain("argument-api-key");
        console.AllText.Should().Contain("[REDACTED]").And.Contain("exit code: 7");
        result.StandardOutput.Should().Be(text);
        result.StandardError.Should().Be(text);
    }

    [Fact]
    public void KnownInheritedTokensAreRedacted()
    {
        // Arrange
        const string name = "ABOX_TEST_INHERITED_TOKEN";
        var previous = Environment.GetEnvironmentVariable(name);
        try
        {
            Environment.SetEnvironmentVariable(name, "inherited-token-value");
            var console = new TestConsole();

            // Act
            new ProcessOutput(console) { Verbose = true }.Write(
                new ProcessRequest("docker", []),
                new ProcessResult(0, "inherited-token-value", "")
            );

            // Assert
            console.AllText.Should().NotContain("inherited-token-value").And.Contain("[REDACTED]");
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, previous);
        }
    }

    [Theory]
    [InlineData("url-password", "query-token")]
    [InlineData("url%2Dpassword", "query%2Dtoken")]
    public void CredentialsInUrlsAreNotEchoedInDiagnostics(string password, string token)
    {
        // Arrange
        var console = new TestConsole();
        var url = $"https://someone:{password}@example.test/mcp?token={token}";

        // Act
        new ProcessOutput(console) { Verbose = true }.Write(
            new ProcessRequest("sbx", ["mcp", "add", "private", "--url", url]),
            new ProcessResult(
                1,
                "",
                $"Cannot connect to {url}: password url-password, token query-token"
            )
        );

        // Assert
        console.AllText.Should().NotContain("url-password").And.NotContain("query-token");
        console.AllText.Should().NotContain(password).And.NotContain(token);
        console.AllText.Should().Contain("[REDACTED]");
    }

    [Fact]
    public void OverlappingCredentialsAreFullyRedactedLongestFirst()
    {
        // Arrange
        var console = new TestConsole();
        var request = new ProcessRequest(
            "tool",
            ["--token", "overlap-secret-extended"],
            StandardInput: "overlap-secret"
        );
        const string text = "before overlap-secret-extended overlap-secret after";
        var result = new ProcessResult(0, text, text);

        // Act
        new ProcessOutput(console) { Verbose = true }.Write(request, result);

        // Assert
        console.Errors.Should().Contain("before [REDACTED] [REDACTED] after");
        console.AllText.Should().NotContain("overlap-secret").And.NotContain("-extended");
        console.Output.Should().BeEmpty();
        result.Should().Be(new ProcessResult(0, text, text));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void MultilineCredentialsAndBareBearerTokensAreRedacted(string newline)
    {
        // Arrange
        var console = new TestConsole();
        var credential =
            $"first-private-line{newline}bEaReR private-bearer-token{newline}last-private-line";
        var request = new ProcessRequest(
            "tool",
            [],
            Environment: new Dictionary<string, string> { ["AUTHORIZATION"] = credential }
        );
        var text =
            $"complete: {credential}; fragments: first-private-line last-private-line private-bearer-token; done";
        var result = new ProcessResult(0, text, text);

        // Act
        new ProcessOutput(console) { Verbose = true }.Write(request, result);

        // Assert
        console
            .Errors.Should()
            .Contain("complete: [REDACTED]; fragments: [REDACTED] [REDACTED] [REDACTED]; done");
        console.AllText.Should().NotContain("private-");
        console.Output.Should().BeEmpty();
        result.Should().Be(new ProcessResult(0, text, text));
    }

    [Fact]
    public void EmptyBearerTokensDoNotBreakDiagnostics()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        new ProcessOutput(console) { Verbose = true }.Write(
            new ProcessRequest("tool", [], StandardInput: "Bearer "),
            new ProcessResult(0, "complete output", "")
        );

        // Assert
        console.AllText.Should().Contain("complete output");
    }

    [Fact]
    public void AttachedConsoleOutputIsNotDuplicated()
    {
        // Arrange
        var console = new TestConsole();

        // Act
        new ProcessOutput(console) { Verbose = true }.Write(
            new ProcessRequest("sbx", ["exec"], AttachConsole: true),
            new ProcessResult(0, "already visible", "already visible")
        );

        // Assert
        console.AllText.Should().BeEmpty();
    }
}
