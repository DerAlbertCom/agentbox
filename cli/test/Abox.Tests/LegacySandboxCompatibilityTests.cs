using System.Security.Cryptography;
using System.Text;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.LocalState;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class LegacySandboxCompatibilityTests
{
    [Theory]
    [InlineData("create", false, false)]
    [InlineData("create", true, false)]
    [InlineData("create", true, true)]
    [InlineData("run", false, true)]
    [InlineData("user-secrets", false, false)]
    [InlineData("user-secrets", true, true)]
    public async Task DiscoveryFailureOrCancellationCannotCreateReplaceOrCopy(
        string command,
        bool legacyLookup,
        bool cancelled
    )
    {
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var legacy = LegacyFolderFixture(cli.Location);
        cli.Configure("""{"userSecrets":[]}""");
        cli.Services.AddSingleton(PublishedSquadTest.Source());
        var failureName = legacyLookup ? legacy : cli.Name;
        Exception error = cancelled
            ? new OperationCanceledException()
            : new AboxException("runtime discovery failed");
        cli.Sbx.FindSandboxAsync(failureName, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<SandboxInfo?>(error));

        (
            await cli.Invoke([
                command,
                "--location",
                cli.Location,
                .. command == "create"
                    ? new[] { "--team", "sample", "--squad", "ac", "--yes" }
                    : [],
            ])
        )
            .Should()
            .Be(cancelled ? 130 : 1);

        if (!legacyLookup)
            await cli.Sbx.DidNotReceive().FindSandboxAsync(legacy, Arg.Any<CancellationToken>());
        await cli
            .Sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
        await cli
            .Sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await cli
            .Sbx.DidNotReceive()
            .ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
        await cli.NoWrites();
        Directory.Exists(Path.Combine(cli.Root.Path, "sandboxes")).Should().BeFalse();
    }

    [Fact]
    public async Task LegacyRunProbesAndHerdrUseTheExistingActualMachineName()
    {
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var legacy = LegacyFolderFixture(cli.Location);
        cli.Sbx.FindSandboxAsync(legacy, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(legacy, "stopped", null));
        cli.Sbx.HasProgramAsync(legacy, "hc", Arg.Any<CancellationToken>()).Returns(true);
        cli.Sbx.GetWorkspaceDirectoryAsync(legacy, Arg.Any<CancellationToken>())
            .Returns("/legacy/workspace");
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new ProcessResult(
                0,
                call.Arg<ProcessRequest>().FileName == "bash" ? cli.Location
                    : call.Arg<ProcessRequest>().FileName == "hc" ? legacy
                    : "{}",
                ""
            ));
        cli.Services.AddSingleton(runner);

        (await cli.Invoke("run", "herdr-app", "--location", cli.Location)).Should().Be(0);

        await cli.Sbx.Received(1).HasProgramAsync(legacy, "hc", Arg.Any<CancellationToken>());
        await runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "herdr-app"
                    && request.Arguments.Contains(legacy)
                    && request.Arguments.Contains("/legacy/workspace")
                ),
                Arg.Any<CancellationToken>()
            );
        await runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("add")),
                Arg.Any<CancellationToken>()
            );
        Directory.Exists(Path.Combine(cli.Root.Path, "sandboxes")).Should().BeFalse();
    }

    [Fact]
    public async Task LegacyDestinationOwnershipRefusalIsStillValueFreeAndCannotWrite()
    {
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var legacy = LegacyFolderFixture(cli.Location);
        cli.Sbx.FindSandboxAsync(legacy, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(legacy, "running", null));
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Store("sample", """{"Password":"synthetic-only"}""");
        cli.Sbx.ValidateUserSecretsDestinationAsync(
                legacy,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromException(new AboxException("wrong owner synthetic-only")));

        (await cli.Run("--force", "--verbose")).Should().Be(1);

        await cli.NoWrites();
        cli.Console.AllText.Should().Contain("destination").And.NotContain("synthetic-only");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RunReplacementConsentKeepsTheSelectedLegacyIdentityThroughCreationCopyAndLaunch(
        bool consent
    )
    {
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var name = LegacyFolderFixture(cli.Location);
        var paths = AboxPaths.For(cli.Root.Path, cli.Root.Path);
        var records = new SandboxRecordStore(paths);
        records.Save(
            name,
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
                BuildCreatedAt = DateTimeOffset.MinValue,
            }
        );
        cli.Root.File("teams/sample/ac.abox", "workload: sample\n");
        new BuildStore(paths).Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                RootDirectory = cli.Root.Path,
                Reference = "agentbox-rwest-sample:latest",
                CreatedAt = DateTimeOffset.UtcNow,
                LocalWorkload = LocalWorkloadTest.Artifact(cli.Root),
            }
        );
        cli.Services.AddSingleton<IProcessRunner>(LocalWorkloadTest.Runner());
        cli.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        cli.Console.Answer = consent;
        if (consent)
            cli.Store("sample", "{}");
        cli.Sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                // A second candidate appearing after selection must not redirect consent.
                cli.Sbx.FindSandboxAsync(cli.Name, Arg.Any<CancellationToken>())
                    .Returns(new SandboxInfo(cli.Name, "running", null));
                return new SandboxInfo(name, "running", null);
            });
        cli.Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        cli.Sbx.RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));

        (await cli.Invoke("run", "--location", cli.Location)).Should().Be(0, cli.Console.AllText);

        if (consent)
        {
            await cli.Sbx.Received(1).RemoveSandboxAsync(name, Arg.Any<CancellationToken>());
            await cli
                .Sbx.DidNotReceive()
                .RemoveSandboxAsync(cli.Name, Arg.Any<CancellationToken>());
            await cli
                .Sbx.Received(1)
                .CreateAsync(
                    Arg.Is<SbxCreateRequest>(request => request.Name == name),
                    Arg.Any<CancellationToken>()
                );
            await cli
                .Sbx.Received(1)
                .WriteUserSecretsAsync(name, "sample", "{}", Arg.Any<CancellationToken>());
            records.Load(name)!.BuildCreatedAt.Should().BeAfter(DateTimeOffset.MinValue);
        }
        else
        {
            await cli
                .Sbx.DidNotReceive()
                .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await cli
                .Sbx.DidNotReceive()
                .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
            await cli.NoWrites();
            records.Load(name)!.BuildCreatedAt.Should().Be(DateTimeOffset.MinValue);
        }
        await cli
            .Sbx.Received(1)
            .ExecAsync(
                Arg.Is<SbxExecRequest>(request => request.Sandbox == name),
                Arg.Any<CancellationToken>()
            );
        records.Load(cli.Name).Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task CreateRequiresConsentToReplaceTheLegacyIdentityAndKeepsItsNameAndState(
        bool consent,
        bool assumeYes
    )
    {
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var name = LegacyFolderFixture(cli.Location);
        cli.Services.AddSingleton(PublishedSquadTest.Source());
        cli.Sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", null));
        cli.Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        cli.Sbx.RemoveSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        cli.Console.Answer = consent;
        cli.Configure("""{"userSecrets":["sample"]}""");
        cli.Store("sample", "{}");
        var sentinel = cli.Root.File($"{name}/.published/kept.txt", "existing-state");

        (
            await cli.Invoke([
                "create",
                "--team",
                "sample",
                "--squad",
                "ac",
                "--location",
                cli.Location,
                .. assumeYes ? new[] { "--yes" } : [],
            ])
        )
            .Should()
            .Be(consent || assumeYes ? 0 : 1);

        if (!assumeYes)
            cli.Console.AllText.Should()
                .Contain($"'{name}' already exists")
                .And.Contain("worktrees");
        File.ReadAllText(sentinel).Should().Be("existing-state");
        var records = new SandboxRecordStore(AboxPaths.For(cli.Root.Path, cli.Root.Path));
        records.Load(cli.Name).Should().BeNull();
        Directory.Exists(Path.Combine(cli.Root.Path, cli.Name)).Should().BeFalse();
        if (consent || assumeYes)
        {
            await cli.Sbx.Received(1).RemoveSandboxAsync(name, Arg.Any<CancellationToken>());
            await cli
                .Sbx.Received(1)
                .CreateAsync(
                    Arg.Is<SbxCreateRequest>(request =>
                        request.Name == name
                        && request.Mounts.All(mount =>
                            mount.HostPath.StartsWith(Path.Combine(cli.Root.Path, name))
                        )
                    ),
                    Arg.Any<CancellationToken>()
                );
            records.Load(name)!.Squad.Should().Be("ac");
            await cli
                .Sbx.Received(1)
                .WriteUserSecretsAsync(name, "sample", "{}", Arg.Any<CancellationToken>());
        }
        else
        {
            await cli
                .Sbx.DidNotReceive()
                .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await cli
                .Sbx.DidNotReceive()
                .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
            records.Load(name).Should().BeNull();
            await cli.NoWrites();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SecretAuthorizationPolicyAndTransferUseTheSameLegacyIdentity(bool hasSquad)
    {
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var name = LegacyFolderFixture(cli.Location);
        cli.Sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "stopped", null));
        cli.Configure(
            hasSquad
                ? """{"squad":"ac","userSecrets":["sample"]}"""
                : """{"userSecrets":["sample"]}"""
        );
        var source = cli.Store(
            "sample",
            """{"Endpoint":"https://localhost:8004/path","Password":"synthetic-only"}"""
        );
        var records = new SandboxRecordStore(AboxPaths.For(cli.Root.Path, cli.Root.Path));
        if (hasSquad)
            records.Save(name, new SandboxRecord { Squad = "ac" });
        cli.Sbx.GetUserSecretsHostPortsAsync(name, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<int>)[8004]);

        (await cli.Run("--verbose")).Should().Be(0);

        await cli.Sbx.Received(1).GetUserSecretsHostPortsAsync(name, Arg.Any<CancellationToken>());
        await cli
            .Sbx.Received(1)
            .ValidateUserSecretsDestinationAsync(
                name,
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            );
        await cli
            .Sbx.Received(1)
            .WriteUserSecretsAsync(
                name,
                "sample",
                Arg.Is<string>(json =>
                    json.Contains("host.docker.internal:8004") && json.Contains("synthetic-only")
                ),
                Arg.Any<CancellationToken>()
            );
        cli.Console.AllText.Should()
            .Contain("Rewrote user-secret store 'sample': 1 keys")
            .And.NotContain("synthetic-only");
        File.ReadAllText(source).Should().Contain("localhost:8004");
        records.Load(cli.Name).Should().BeNull();
        records.Load(name)?.Squad.Should().Be(hasSquad ? "ac" : null);
    }

    // Test-data oracle for a temporary ordinary directory named 'folder'. The literal
    // path/name fixtures below independently pin the historical hash/sanitization contract.
    private static string LegacyFolderFixture(string location) =>
        "folder-ai-sbx-"
        + Convert
            .ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(location)))[..6]
            .ToLowerInvariant();

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("mismatched")]
    public async Task LegacyDiscoveryDoesNotGrantSquadTrustEvenWithForceOrAPreferredNameRecord(
        string recordState
    )
    {
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var name = LegacyFolderFixture(cli.Location);
        cli.Sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", null));
        cli.Configure("""{"squad":"ac","userSecrets":["missing-store"]}""");
        var records = new SandboxRecordStore(AboxPaths.For(cli.Root.Path, cli.Root.Path));
        records.Save(cli.Name, new SandboxRecord { Squad = "ac" });
        if (recordState != "missing")
            records.Save(
                name,
                new SandboxRecord { Squad = recordState == "unknown" ? "" : "other" }
            );

        (await cli.Run("--force")).Should().Be(1);

        cli.Console.AllText.Should()
            .Contain(recordState == "mismatched" ? "squad" : "Cannot verify")
            .And.NotContain("missing-store");
        await cli.NoWrites();
        await cli
            .Sbx.DidNotReceive()
            .GetUserSecretsHostPortsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        if (recordState == "missing")
            records.Load(name).Should().BeNull();
    }

    [Theory]
    [InlineData("running")]
    [InlineData("stopped")]
    public async Task RunReusesTheExactLegacySandboxWithoutCreatingOrAdoptingIt(string status)
    {
        using var cli = new UserSecretsCliHarness(existingSandbox: false);
        var location = LegacySandboxFixture.Location;
        var name = LegacySandboxFixture.Name;
        cli.Sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, status, null));
        cli.Sbx.GetWorkspaceDirectoryAsync(name, Arg.Any<CancellationToken>())
            .Returns("/legacy/workspace");

        (await cli.Invoke("run", "--location", location)).Should().Be(0);

        await cli.Sbx.Received(1).GetWorkspaceDirectoryAsync(name, Arg.Any<CancellationToken>());
        await cli
            .Sbx.Received(1)
            .ExecAsync(
                Arg.Is<SbxExecRequest>(request =>
                    request.Sandbox == name && request.WorkingDirectory == "/legacy/workspace"
                ),
                Arg.Any<CancellationToken>()
            );
        await cli
            .Sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
        await cli
            .Sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        Directory.Exists(Path.Combine(cli.Root.Path, "sandboxes")).Should().BeFalse();
    }
}
