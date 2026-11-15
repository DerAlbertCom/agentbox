using Abox.Configuration;
using Abox.Features;
using Abox.Infrastructure;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sandboxes;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsLifecycleTests
{
    [Theory]
    [InlineData("published", false)]
    [InlineData("local", false)]
    [InlineData("run", false)]
    [InlineData("published", true)]
    [InlineData("local", true)]
    [InlineData("run", true)]
    public async Task AutomaticCopyExpandsTheSameHostPrefixesAndNeverForcesDeniedExpandedStores(
        string entry,
        bool denied
    )
    {
        using var fixture = new Fixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["Cape.*"]}""");
        fixture.Store("Cape.Deals", "{}");
        fixture.Store(
            "Cape.Position",
            denied ? """{"Endpoint":"https://PROD.EXAMPLE/synthetic-private"}""" : "{}"
        );
        fixture.Store("Other", "invalid unselected JSON");

        (await fixture.Replace(entry)).Should().Be(denied ? 1 : 0, fixture.Console.AllText);

        if (denied)
        {
            await fixture.NoReplacementOrCopy();
            fixture.Console.AllText.Should().Contain("Cape.Position").And.Contain("--force");
        }
        else
        {
            await fixture
                .Sbx.Received(1)
                .WriteUserSecretsAsync(
                    fixture.Name,
                    "Cape.Deals",
                    "{}",
                    Arg.Any<CancellationToken>()
                );
            await fixture
                .Sbx.Received(1)
                .WriteUserSecretsAsync(
                    fixture.Name,
                    "Cape.Position",
                    "{}",
                    Arg.Any<CancellationToken>()
                );
        }
        fixture
            .Console.AllText.Should()
            .NotContain("PROD.EXAMPLE")
            .And.NotContain("synthetic-private");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreationUsesTheFoldersSquadAndCopiesOnlySelectedStoresAfterSavingTheRecord(
        bool local
    )
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        var source = fixture.Store("sample", """{"Password":"synthetic-only"}""");
        fixture.Store("unselected", "not JSON");
        fixture
            .Sbx.WriteUserSecretsAsync(
                fixture.Name,
                "sample",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                fixture.Records.Load(fixture.Name)!.Squad.Should().Be("ac");
                fixture.Console.AllText.Should().NotContain("Created sandbox");
                return Task.CompletedTask;
            });

        (await fixture.Create(local)).Should().Be(0);

        await fixture
            .Sbx.Received(1)
            .WriteUserSecretsAsync(
                fixture.Name,
                "sample",
                Arg.Is<string>(json => json.Contains("synthetic-only")),
                Arg.Any<CancellationToken>()
            );
        File.ReadAllText(source).Should().Be("""{"Password":"synthetic-only"}""");
        fixture
            .Console.AllText.Should()
            .Contain("1 keys")
            .And.Contain("Created sandbox")
            .And.NotContain("synthetic-only");
    }

    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task FailedPostCreationCopyRetainsTheNewSandboxRecordAndOffersRetryWithoutLaunching(
        string entry
    )
    {
        using var fixture = new Fixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromException(new IOException("synthetic-private-diagnostic")));

        (await fixture.Replace(entry)).Should().Be(1);

        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().BeAfter(Fixture.OldBuild);
        await fixture
            .Sbx.Received(1)
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
        await fixture
            .Sbx.Received(1)
            .RemoveSandboxAsync(fixture.Name, Arg.Any<CancellationToken>());
        await fixture
            .Sbx.DidNotReceive()
            .ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
        fixture
            .Console.AllText.Should()
            .Contain("sandbox was created")
            .And.Contain($"abox user-secrets --location \"{fixture.Location}\"")
            .And.NotContain("Created sandbox '")
            .And.NotContain("Open it with")
            .And.NotContain("synthetic-private-diagnostic");
    }

    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task AConflictingSquadCannotReplaceOrAutomaticallyCopy(string entry)
    {
        using var fixture = new Fixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"bd","userSecrets":["missing"]}""");
        var exit = await fixture.Replace(entry);

        exit.Should().Be(1);
        fixture.Console.AllText.Should().Contain("squad").And.Contain("abox.json");
        await fixture.NoReplacementOrCopy();
        await fixture
            .Sbx.DidNotReceive()
            .ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("published", true)]
    [InlineData("local", true)]
    [InlineData("run", true)]
    [InlineData("published", false)]
    [InlineData("local", false)]
    [InlineData("run", false)]
    public async Task ReplacementUsesTheFolderConfigurationValidatedAtCommandEntry(
        string entry,
        bool configured
    )
    {
        using var fixture = new Fixture();
        fixture.Existing();
        if (configured)
            fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        if (entry == "run")
            fixture
                .Sbx.FindSandboxAsync(fixture.Name, Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    fixture.Configure("""{"squad":"bd","userSecrets":["missing"]}""");
                    return new SandboxInfo(fixture.Name, "running", Fixture.OldBuild);
                });
        else
            fixture
                .Sbx.GetVersionAsync(Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    fixture.Configure("""{"squad":"bd","userSecrets":["missing"]}""");
                    return new Version(0, 51, 0);
                });

        (await fixture.Replace(entry)).Should().Be(0);

        fixture.Records.Load(fixture.Name)!.Squad.Should().Be("ac");
        if (configured)
            await fixture
                .Sbx.Received(1)
                .WriteUserSecretsAsync(fixture.Name, "sample", "{}", Arg.Any<CancellationToken>());
        else
            await fixture.NoCopy();
    }

    [Fact]
    public async Task CancellationAfterReplacementConsentDoesNotRemoveOrCreateASandbox()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.FindSandboxAsync(fixture.Name, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellation.Cancel();
                return new SandboxInfo(fixture.Name, "running", Fixture.OldBuild);
            });

        (await fixture.CreateShared(cancellation.Token)).Should().Be(130);

        await fixture.NoReplacementOrCopy();
        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().Be(Fixture.OldBuild);
        fixture.Console.AllText.Should().NotContain("Created sandbox");
    }

    [Theory]
    [InlineData("published", "missing")]
    [InlineData("published", "malformed")]
    [InlineData("published", "denied")]
    [InlineData("local", "missing")]
    [InlineData("local", "malformed")]
    [InlineData("local", "denied")]
    [InlineData("run", "missing")]
    [InlineData("run", "malformed")]
    [InlineData("run", "denied")]
    public async Task EverySelectedHostStoreIsValidatedBeforeEitherReplacementEntryPointEvenWithYes(
        string entry,
        string problem
    )
    {
        using var fixture = new Fixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["first","later"]}""");
        fixture.Store("first", """{"Password":"synthetic-safe"}""");
        if (problem != "missing")
            fixture.Store(
                "later",
                problem == "malformed"
                    ? "{\"Password\":\"synthetic-private\", broken}"
                    : """{"Endpoint":"https://PROD.EXAMPLE/synthetic-private"}"""
            );

        (await fixture.Replace(entry)).Should().Be(1);

        await fixture.NoReplacementOrCopy();
        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().Be(Fixture.OldBuild);
        fixture
            .Console.AllText.Should()
            .Contain("later")
            .And.NotContain("synthetic-private")
            .And.NotContain("PROD.EXAMPLE")
            .And.NotContain("Created sandbox");
        if (problem == "denied")
            fixture.Console.AllText.Should().Contain("Endpoint").And.Contain("--force");
    }

    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task BothReplacementEntryPointsUseTheSameCopyFlowBeforeLaunching(string entry)
    {
        using var fixture = new Fixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"AC","userSecrets":["sample"]}""");
        fixture.Store("sample", """{"Endpoint":"https://localhost:8004/path"}""");
        var copied = false;
        fixture
            .Sbx.WriteUserSecretsAsync(
                fixture.Name,
                "sample",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                copied = true;
                return Task.CompletedTask;
            });
        fixture
            .Sbx.ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                copied.Should().BeTrue();
                return 0;
            });

        (await fixture.Replace(entry)).Should().Be(0);

        copied.Should().BeTrue();
        await fixture
            .Sbx.Received(1)
            .RemoveSandboxAsync(fixture.Name, Arg.Any<CancellationToken>());
        fixture
            .Console.AllText.Should()
            .Contain("Copied user-secret store 'sample'")
            .And.NotContain("Safety override");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitSquadCanMatchTheFolderCaseInsensitively(bool local)
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");

        (await fixture.Create(local, "--squad", "AC")).Should().Be(0);

        fixture.Records.Load(fixture.Name)!.Squad.Should().Be("ac");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithoutFolderConfigurationCreationNeverDiscoversOrCopiesSecrets(bool local)
    {
        using var fixture = new Fixture();
        fixture.Store("sample", "not JSON");

        (await fixture.Create(local, "--squad", "ac")).Should().Be(0);

        await fixture.NoCopy();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryRunDoesNotReadOrResyncSelectedHostSecrets(bool configured)
    {
        using var fixture = new Fixture();
        fixture.Existing();
        if (configured)
            fixture.Configure("""{"squad":"AC","userSecrets":["missing"]}""");

        (await fixture.OrdinaryRun()).Should().Be(0);

        await fixture.NoReplacementOrCopy();
        await fixture
            .Sbx.Received(1)
            .ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("mismatched")]
    [InlineData("missing")]
    [InlineData("unknown")]
    public async Task OrdinaryRunCanOpenAnExistingSandboxWithoutAMatchingFolderSquadRecord(
        string recordState
    )
    {
        using var fixture = new Fixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"bd","userSecrets":["missing"]}""");
        var record = fixture.Records.Load(fixture.Name)!;
        record.BuildCreatedAt = DateTimeOffset.MaxValue;
        if (recordState == "unknown")
            record.Squad = "";
        fixture.Records.Save(fixture.Name, record);
        if (recordState == "missing")
            fixture.Records.Remove(fixture.Name);

        (await fixture.Run()).Should().Be(0);

        await fixture.NoReplacementOrCopy();
        await fixture
            .Sbx.Received(1)
            .ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DecliningEitherReplacementDoesNotCopySecrets(bool run)
    {
        using var fixture = new Fixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture.Console.Answer = false;

        (await (run ? fixture.Run() : fixture.Create(false, "--squad", "ac")))
            .Should()
            .Be(run ? 0 : 1);

        await fixture.NoReplacementOrCopy();
        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().Be(Fixture.OldBuild);
    }

    [Fact]
    public async Task DecliningANewerBuildCanOpenTheExistingSandboxDespiteADifferentFolderSquad()
    {
        using var fixture = new Fixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"bd","userSecrets":["missing"]}""");
        fixture.Console.Answer = false;

        (await fixture.Run()).Should().Be(0);

        await fixture.NoReplacementOrCopy();
        await fixture
            .Sbx.Received(1)
            .ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedCreationDoesNotSaveANewRecordOrAttemptSecretCopy()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(1, "synthetic creation failure"));

        (await fixture.Create()).Should().Be(1);

        fixture.Records.Load(fixture.Name).Should().BeNull();
        await fixture.NoCopy();
        fixture.Console.AllText.Should().NotContain("Created sandbox");
    }

    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task CancelledPostCreateCopyKeepsTheNewRecordAndReturns130WithoutLaunch(
        string entry
    )
    {
        using var fixture = new Fixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromException(new OperationCanceledException("synthetic-private")));

        (await fixture.Replace(entry)).Should().Be(130);

        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().BeAfter(Fixture.OldBuild);
        fixture
            .Console.AllText.Should()
            .Contain("record retained")
            .And.Contain("abox user-secrets --location")
            .And.NotContain("synthetic-private")
            .And.NotContain("Created sandbox");
        await fixture
            .Sbx.DidNotReceive()
            .ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task ReplacementWithoutFolderConfigurationNeverCopiesSecrets(string entry)
    {
        using var fixture = new Fixture();
        fixture.Existing();

        (await fixture.Replace(entry)).Should().Be(0);

        await fixture.NoCopy();
    }

    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task AnUnreadableLaterHostStoreCannotRemoveTheExistingSandbox(string entry)
    {
        using var fixture = new Fixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["first","later"]}""");
        fixture.Store("first", "{}");
        using var locked = new FileStream(
            fixture.Store("later", "{}"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None
        );

        (await fixture.Replace(entry)).Should().Be(1);

        await fixture.NoReplacementOrCopy();
        fixture.Console.AllText.Should().Contain("later").And.Contain("permissions");
    }

    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task InvalidFolderConfigurationCannotRemoveTheExistingSandbox(string entry)
    {
        using var fixture = new Fixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample",12]}""");

        (await fixture.Replace(entry)).Should().Be(1);

        await fixture.NoReplacementOrCopy();
    }

    [Fact]
    public async Task IntentionalOverrideUsesOnDemandForceAndNeverReplacementConsent()
    {
        using var fixture = new Fixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", """{"Endpoint":"https://PROD.EXAMPLE/synthetic-private"}""");

        (await fixture.Create(false, "--yes")).Should().Be(1);
        await fixture.NoReplacementOrCopy();
        (await fixture.OnDemand("--force")).Should().Be(0);

        fixture.Console.AllText.Should().Contain("Safety override").And.NotContain("PROD.EXAMPLE");
        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().Be(Fixture.OldBuild);
    }

    [Fact]
    public async Task MissingSandboxCreateHintUsesTheFolderSquadWithoutLocalSources()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"squad":"ac"}""");

        (await fixture.Run()).Should().Be(1);

        fixture.Console.AllText.Should().Contain("--squad ac").And.NotContain("--squad <name>");
        await fixture.NoReplacementOrCopy();
    }

    [Fact]
    public async Task CancellationAtTheEndOfTransferDoesNotReportFullCreationSuccess()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                cancellation.Cancel();
                return Task.CompletedTask;
            });

        (await fixture.CreateShared(cancellation.Token)).Should().Be(130);

        fixture.Records.Load(fixture.Name).Should().NotBeNull();
        fixture
            .Console.AllText.Should()
            .Contain("record retained")
            .And.NotContain("Created sandbox");
    }

    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task FailedTargetPreparationKeepsTheNewSandboxAndDoesNotLaunchOrWrite(string entry)
    {
        using var fixture = new Fixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.ValidateUserSecretsDestinationAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromException(new IOException("synthetic-private-diagnostic")));

        (await fixture.Replace(entry)).Should().Be(1);

        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().BeAfter(Fixture.OldBuild);
        fixture
            .Console.AllText.Should()
            .Contain("record retained")
            .And.Contain("abox user-secrets --location")
            .And.NotContain("synthetic-private-diagnostic")
            .And.NotContain("Created sandbox");
        await fixture
            .Sbx.DidNotReceive()
            .WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Sbx.DidNotReceive()
            .ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CancellationAfterCreationStillSavesTheNewRecordButStartsNoTransfer()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellation.Cancel();
                return new SbxOutcome(0, "");
            });

        (await fixture.CreateShared(cancellation.Token)).Should().Be(130);

        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().BeAfter(Fixture.OldBuild);
        await fixture.NoCopy();
        fixture
            .Console.AllText.Should()
            .Contain("record retained")
            .And.NotContain("Created sandbox");
    }

    [Fact]
    public async Task CancellationDuringCreationDoesNotStartASubsequentCopy()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<SbxOutcome>(new OperationCanceledException("synthetic-private"))
            );

        (await fixture.Create()).Should().Be(130);

        await fixture.NoCopy();
        fixture.Records.Load(fixture.Name).Should().BeNull();
        fixture
            .Console.AllText.Should()
            .NotContain("synthetic-private")
            .And.NotContain("Created sandbox");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly UserSecretsCliHarness cli = new(existingSandbox: false);
        public TempDirectory Root => cli.Root;
        public TestConsole Console => cli.Console;
        public ISbxClient Sbx => cli.Sbx;
        public string Location => cli.Location;
        public string Name => cli.Name;
        public SandboxRecordStore Records { get; }
        public BuildStore Builds { get; }
        public static readonly DateTimeOffset OldBuild = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public Fixture()
        {
            Root.File("teams/sample/ac.abox", "workload: sample\n");
            Root.File("teams/sample/bd.abox", "workload: sample\n");
            var paths = AboxPaths.For(Root.Directory_("config"), Root.Directory_("state"));
            Records = new SandboxRecordStore(paths);
            Builds = new BuildStore(paths);
            Builds.Save(
                new BuildManifest
                {
                    Squad = "ac",
                    Tag = "latest",
                    RootDirectory = Root.Path,
                    Reference = ImageName.OfTeam("sample", "latest"),
                    CreatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                    LocalWorkload = LocalWorkloadTest.Artifact(Root),
                }
            );
            Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
                .Returns(new SbxOutcome(0, ""));
            Sbx.RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(new SbxOutcome(0, ""));
            Sbx.ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>()).Returns(0);
            var services = cli.Services;
            services.AddSingleton(
                new AboxConfig
                {
                    DefaultTeam = "sample",
                    DefaultLocation = Location,
                    UserSecretsDenyList = ["prod.example"],
                }
            );
            services.AddSingleton(paths);
            services.AddSingleton<IProcessRunner>(LocalWorkloadTest.Runner());
            services.AddSingleton(PublishedSquadTest.Source());
        }

        public void Configure(string json) => cli.Configure(json);

        public string Store(string id, string json) => cli.Store(id, json);

        public Task<int> Create(bool local = false, params string[] args) =>
            cli.Invoke([
                "create",
                .. local ? new[] { "--local", "--root", Root.Path } : [],
                .. args,
            ]);

        public void Existing()
        {
            Records.Save(
                Name,
                new SandboxRecord
                {
                    Team = "sample",
                    Squad = "ac",
                    Tag = "latest",
                    BuildCreatedAt = OldBuild,
                }
            );
            cli.ExistingSandbox(OldBuild);
        }

        public Task<int> Replace(string entry) =>
            entry == "run" ? Run() : Create(entry == "local", "--squad", "ac", "--yes");

        public Task<int> Run() => cli.Invoke("run");

        public Task<int> OnDemand(params string[] args) => cli.Invoke(["user-secrets", .. args]);

        public Task<int> CreateShared(CancellationToken cancellationToken) =>
            CommandHandling.RunAsync(
                async () =>
                    await cli
                        .Provider.GetRequiredService<SandboxCreator>()
                        .CreateAsync(
                            new SandboxCreationRequest(
                                new("sample", "", ["ac"]),
                                "ac",
                                Location,
                                "latest",
                                await FolderConfig.LoadAsync(Location, cancellationToken),
                                AssumeYes: true
                            ),
                            cancellationToken
                        ),
                Console
            );

        public Task<int> OrdinaryRun()
        {
            var record = Records.Load(Name)!;
            record.BuildCreatedAt = DateTimeOffset.MaxValue;
            Records.Save(Name, record);
            return Run();
        }

        public async Task NoReplacementOrCopy()
        {
            await Sbx.DidNotReceive()
                .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await Sbx.DidNotReceive()
                .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
            await NoCopy();
        }

        public async Task NoCopy()
        {
            await Sbx.DidNotReceive()
                .GetUserSecretsHostPortsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
            await Sbx.DidNotReceive()
                .ValidateUserSecretsDestinationAsync(
                    Arg.Any<string>(),
                    Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<CancellationToken>()
                );
            await Sbx.DidNotReceive()
                .WriteUserSecretsAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                );
        }

        public void Dispose() => cli.Dispose();
    }
}
