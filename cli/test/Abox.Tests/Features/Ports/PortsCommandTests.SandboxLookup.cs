using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Abox.Infrastructure;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed partial class PortsCommandTests
{
    [Theory]
    [InlineData("ports", false)]
    [InlineData("ports", true)]
    [InlineData("user-secrets", false)]
    [InlineData("user-secrets", true)]
    public async Task LegacyWorkspaceLookupSurvivesWindowsDriveCaseAtTheComposedCli(
        string command,
        bool lowercaseDrive
    )
    {
        // Arrange
        RequireWindowsDriveLookup();
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        if (command == "user-secrets")
            cli.Configure("{}");
        var original = char.ToUpperInvariant(cli.Location[0]) + cli.Location[1..];
        // Independent historical fixture oracle, as in LegacySandboxCompatibilityTests.
        var name =
            "folder-ai-sbx-"
            + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(original)))[..6];
        var calls = UseLegacyLookupInventory(cli, original, name);
        var location = lowercaseDrive
            ? char.ToLowerInvariant(original[0]) + original[1..]
            : original;

        // Act
        var exit = await cli.Invoke(command, "--location", location);

        // Assert
        calls
            .Should()
            .OnlyContain(call => new[] { "version", "diagnose", "ls" }.Contains(call.Arguments[0]));
        exit.Should().Be(0, cli.Console.AllText);
        cli.Console.AllText.Should().NotContain($"There is no sandbox for '{location}'.");
    }

    [Theory]
    [InlineData(@"D:\dev\rwe")]
    [InlineData(@"D:\dev\rwe\")]
    [InlineData(@"d:\dev\rwe")]
    [InlineData(@"d:\dev\rwe\")]
    public async Task RecordedRweLegacyInventoryIsRecognizedByTheComposedPortsLookup(
        string location
    )
    {
        // Arrange
        RequireWindowsDriveLookup();
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        // Reconstructed from the reported identity/workspace, not a live inventory query.
        var calls = UseLegacyLookupInventory(cli, @"D:\dev\rwe", "rwe-ai-sbx-41bc30");

        // Act: refresh skips reading the real workspace, but uses the same ports lookup.
        // Its transport is substituted; no live refresh or other sbx operation can run.
        var exit = await cli.Invoke("ports", "--location", location, "--refresh-https-bootstrap");

        // Assert
        exit.Should().Be(0, cli.Console.AllText);
        cli.Console.AllText.Should()
            .NotContain(
                $"There is no sandbox for '{location}'. Run 'abox create --location \"{location}\" --team <name> --squad <name>' first; ports never creates or replaces one."
            );
        calls
            .Single(call => call.Arguments[0] == "exec")
            .Arguments.Should()
            .Contain("rwe-ai-sbx-41bc30");
    }

    private static void RequireWindowsDriveLookup()
    {
        if (!OperatingSystem.IsWindows())
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value + "Requires Windows drive-letter path semantics."
            );
    }

    private static List<ProcessRequest> UseLegacyLookupInventory(
        UserSecretsCliHarness cli,
        string originalLocation,
        string name
    )
    {
        var calls = new List<ProcessRequest>();
        var inventory = $$"""
            [{"name":{{JsonSerializer.Serialize(
                name
            )}},"status":"running","template":"cao-docker-ai-sandbox","workspaces":[{{JsonSerializer.Serialize(
                originalLocation
            )}}]}]
            """;
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                calls.Add(request);
                request.FileName.Should().Be("sbx");
                var output = request.Arguments[0] switch
                {
                    "version" => """{"client":{"version":"0.51.0"}}""",
                    "diagnose" => """{"checks":[]}""",
                    "ls" => inventory,
                    "exec" when request.Arguments.Contains("refresh-bootstrap") => "",
                    _ => throw new InvalidOperationException("Unexpected runtime operation."),
                };
                return new ProcessResult(0, output, "");
            });
        cli.Services.AddSingleton(runner);
        cli.Services.AddSingleton<ISbxClient, SbxClient>();
        return calls;
    }
}
