using System.Text.Json;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.LocalState;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

/// <summary>"This folder's Sandbox" for run, ports and user-secrets through the real sbx adapter.</summary>
public sealed class FolderSandboxCommandTests
{
    [Theory]
    [InlineData("run", null)]
    [InlineData("ports", "ports never creates or replaces one")]
    [InlineData("user-secrets", "user-secrets never creates or replaces one")]
    public async Task MissingSandboxPrintsTheFullCreateRecoveryHint(string command, string? note)
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Record(
            new SandboxRecord
            {
                Team = "recorded",
                Squad = "ac",
                Tag = "release",
                KitReference = "published-set",
            }
        );

        // Act
        var exitCode = await fixture.Invoke(command);

        // Assert
        exitCode.Should().Be(1);
        var output = fixture.Cli.Console.AllText;
        output
            .Should()
            .Contain($"There is no sandbox for '{fixture.Cli.Location}'")
            .And.Contain(
                $"Create it with: abox create --team recorded --squad ac --tag release --location \"{fixture.Cli.Location}\""
            );
        if (note is null)
            output.Should().NotContain("never creates or replaces");
        else
            output.Should().Contain(note);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("ports")]
    [InlineData("user-secrets")]
    public async Task BrokenBuildMetadataGivesADiagnosticAndARemoteRecoveryHint(string command)
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Record(
            new SandboxRecord
            {
                Team = "recorded",
                Squad = "ac",
                Tag = "release",
            }
        );
        fixture.Cli.Root.File("builds/recorded/ac/release/build.json", "{ broken JSON");

        // Act
        var exitCode = await fixture.Invoke(command);

        // Assert
        exitCode.Should().Be(1);
        var output = fixture.Cli.Console.AllText;
        output
            .Should()
            .Contain("abox build --team recorded --tag release")
            .And.Contain("abox create --team recorded --squad ac --tag release --location")
            .And.NotContain("--local");
        output
            .IndexOf("abox build", StringComparison.Ordinal)
            .Should()
            .BeLessThan(output.IndexOf("abox create", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("run")]
    [InlineData("ports")]
    [InlineData("user-secrets")]
    public async Task EachLookupListsSandboxesOnce(string command)
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        await fixture.Invoke(command);

        // Assert
        fixture.Listings.Should().Be(1);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("ports", "--refresh-https-bootstrap")]
    [InlineData("user-secrets")]
    public async Task LegacySandboxIsFound(string command, params string[] args)
    {
        // Arrange
        using var fixture = new Fixture();
        // Independent historical oracle, as in LegacySandboxCompatibilityTests.
        var legacy =
            "folder-ai-sbx-"
            + Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(fixture.Cli.Location)
                )
            )[..6];
        fixture.Sandbox("unrelated-ai-sbx-000000");
        fixture.Sandbox(legacy);

        // Act
        var exitCode = await fixture.Invoke(command, args);

        // Assert
        exitCode.Should().Be(0, fixture.Cli.Console.AllText);
        fixture.Listings.Should().Be(1);
        if (command != "user-secrets")
            fixture
                .Calls.Where(call => call.FileName == "sbx" && call.Arguments[0] == "exec")
                .Should()
                .NotBeEmpty()
                .And.OnlyContain(call => call.Arguments.Contains(legacy));
    }

    private sealed class Fixture : IDisposable
    {
        private readonly List<SandboxInfo> inventory = [];

        public Fixture()
        {
            Cli.Configure("{}");
            var runner = Substitute.For<IProcessRunner>();
            runner
                .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                    Respond(call.Arg<ProcessRequest>(), call.Arg<CancellationToken>())
                );
            Cli.Services.AddSingleton(runner);
            Cli.Services.AddSingleton<ISbxClient, SbxClient>();
        }

        public UserSecretsCliHarness Cli { get; } =
            new(
                new AboxConfig { DefaultTeam = "default-team", DefaultTag = "default-tag" },
                existingSandbox: false
            );

        public List<ProcessRequest> Calls { get; } = [];

        public int Listings =>
            Calls.Count(call => call.FileName == "sbx" && call.Arguments[0] == "ls");

        public void Sandbox(string name, string? workspace = null) =>
            inventory.Add(
                new SandboxInfo(name, "running", null)
                {
                    Workspaces = workspace is null ? [] : [workspace],
                }
            );

        public void Record(SandboxRecord record) =>
            Cli.Provider.GetRequiredService<SandboxRecordStore>().Save(Cli.Name, record);

        public Task<int> Invoke(string command, params string[] args) =>
            Cli.Invoke([command, "--location", Cli.Location, .. args]);

        private async Task<ProcessResult> Respond(
            ProcessRequest request,
            CancellationToken cancellationToken
        )
        {
            Calls.Add(request);
            if (request.FileName != "sbx")
                return await new ProcessRunner().RunAsync(request, cancellationToken);
            var output = request.Arguments[0] switch
            {
                "version" => """{"client":{"version":"0.51.0"}}""",
                "diagnose" => """{"checks":[]}""",
                "ls" => JsonSerializer.Serialize(
                    inventory.Select(sandbox => new
                    {
                        name = sandbox.Name,
                        status = sandbox.Status,
                        workspaces = sandbox.Workspaces,
                    })
                ),
                _ => "",
            };
            return new ProcessResult(0, output, "");
        }

        public void Dispose() => Cli.Dispose();
    }
}
