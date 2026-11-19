using Abox.Configuration;
using Abox.Features;
using Abox.Infrastructure;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sandboxes;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using static Abox.Tests.UserSecretsLifecycleFixture;

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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["Cape.*"]}""");
        fixture.Store("Cape.Deals", "{}");
        fixture.Store(
            "Cape.Position",
            denied ? """{"Endpoint":"https://PROD.EXAMPLE/synthetic-private"}""" : "{}"
        );
        fixture.Store("Other", "invalid unselected JSON");

        // Act
        var exitCode = await fixture.Replace(entry);

        // Assert
        exitCode.Should().Be(denied ? 1 : 0, fixture.Console.AllText);
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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
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

        // Act
        var exitCode = await fixture.Create(local);

        // Assert
        exitCode.Should().Be(0);
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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
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

        // Act
        var exitCode = await fixture.Replace(entry);

        // Assert
        exitCode.Should().Be(1);
        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().BeAfter(OldBuild);
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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"bd","userSecrets":["missing"]}""");

        // Act
        var exit = await fixture.Replace(entry);

        // Assert
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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
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
                    return new SandboxInfo(fixture.Name, "running", OldBuild);
                });
        else
            fixture
                .Sbx.GetVersionAsync(Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    fixture.Configure("""{"squad":"bd","userSecrets":["missing"]}""");
                    return new Version(0, 51, 0);
                });

        // Act
        var exitCode = await fixture.Replace(entry);

        // Assert
        exitCode.Should().Be(0);
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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.FindSandboxAsync(fixture.Name, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cancellation.Cancel();
                return new SandboxInfo(fixture.Name, "running", OldBuild);
            });

        // Act
        var exitCode = await fixture.CreateShared(cancellation.Token);

        // Assert
        exitCode.Should().Be(130);
        await fixture.NoReplacementOrCopy();
        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().Be(OldBuild);
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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
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

        // Act
        var exitCode = await fixture.Replace(entry);

        // Assert
        exitCode.Should().Be(1);
        await fixture.NoReplacementOrCopy();
        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().Be(OldBuild);
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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
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

        // Act
        var exitCode = await fixture.Replace(entry);

        // Assert
        exitCode.Should().Be(0);
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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");

        // Act
        var exitCode = await fixture.Create(local, "--squad", "AC");

        // Assert
        exitCode.Should().Be(0);
        fixture.Records.Load(fixture.Name)!.Squad.Should().Be("ac");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WithoutFolderConfigurationCreationNeverDiscoversOrCopiesSecrets(bool local)
    {
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Store("sample", "not JSON");

        // Act
        var exitCode = await fixture.Create(local, "--squad", "ac");

        // Assert
        exitCode.Should().Be(0);
        await fixture.NoCopy();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrdinaryRunDoesNotReadOrResyncSelectedHostSecrets(bool configured)
    {
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Existing();
        if (configured)
            fixture.Configure("""{"squad":"AC","userSecrets":["missing"]}""");
        fixture.Root.File("folder/Api.csproj", "not XML: ordinary run must not discover projects");

        // Act
        var exitCode = await fixture.OrdinaryRun();

        // Assert
        exitCode.Should().Be(0);
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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"bd","userSecrets":["missing"]}""");
        var record = fixture.Records.Load(fixture.Name)!;
        record.BuildCreatedAt = DateTimeOffset.MaxValue;
        if (recordState == "unknown")
            record.Squad = "";
        fixture.Records.Save(fixture.Name, record);
        if (recordState == "missing")
            File.Delete(Path.Combine(fixture.Records.RecordsDirectory, fixture.Name + ".json"));

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(0);
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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture.Console.Answer = false;

        // Act
        var exitCode = await (run ? fixture.Run() : fixture.Create(false, "--squad", "ac"));

        // Assert
        exitCode.Should().Be(run ? 0 : 1);
        await fixture.NoReplacementOrCopy();
        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().Be(OldBuild);
    }

    [Fact]
    public async Task DecliningANewerBuildCanOpenTheExistingSandboxDespiteADifferentFolderSquad()
    {
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"bd","userSecrets":["missing"]}""");
        fixture.Console.Answer = false;

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(0);
        await fixture.NoReplacementOrCopy();
        await fixture
            .Sbx.Received(1)
            .ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FailedCreationDoesNotSaveANewRecordOrAttemptSecretCopy()
    {
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(1, "synthetic creation failure"));

        // Act
        var exitCode = await fixture.Create();

        // Assert
        exitCode.Should().Be(1);
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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
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

        // Act
        var exitCode = await fixture.Replace(entry);

        // Assert
        exitCode.Should().Be(130);
        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().BeAfter(OldBuild);
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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Existing();

        // Act
        var exitCode = await fixture.Replace(entry);

        // Assert
        exitCode.Should().Be(0);
        await fixture.NoCopy();
    }

    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task AnUnreadableLaterHostStoreCannotRemoveTheExistingSandbox(string entry)
    {
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["first","later"]}""");
        fixture.Store("first", "{}");
        using var locked = new FileStream(
            fixture.Store("later", "{}"),
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None
        );

        // Act
        var exitCode = await fixture.Replace(entry);

        // Assert
        exitCode.Should().Be(1);
        await fixture.NoReplacementOrCopy();
        fixture.Console.AllText.Should().Contain("later").And.Contain("permissions");
    }

    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task InvalidFolderConfigurationCannotRemoveTheExistingSandbox(string entry)
    {
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample",12]}""");

        // Act
        var exitCode = await fixture.Replace(entry);

        // Assert
        exitCode.Should().Be(1);
        await fixture.NoReplacementOrCopy();
    }

    [Fact]
    public async Task IntentionalOverrideUsesOnDemandForceAndNeverReplacementConsent()
    {
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Existing();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", """{"Endpoint":"https://PROD.EXAMPLE/synthetic-private"}""");

        // Act
        var replacementExitCode = await fixture.Create(false, "--yes");

        // Assert
        replacementExitCode.Should().Be(1);
        await fixture.NoReplacementOrCopy();

        // Act
        var onDemandExitCode = await fixture.OnDemand("--force");

        // Assert
        onDemandExitCode.Should().Be(0);
        fixture.Console.AllText.Should().Contain("Safety override").And.NotContain("PROD.EXAMPLE");
        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().Be(OldBuild);
    }

    [Fact]
    public async Task MissingSandboxCreateHintUsesTheFolderSquadWithoutLocalSources()
    {
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Configure("""{"squad":"ac"}""");

        // Act
        var exitCode = await fixture.Run();

        // Assert
        exitCode.Should().Be(1);
        fixture.Console.AllText.Should().Contain("--squad ac").And.NotContain("--squad <name>");
        await fixture.NoReplacementOrCopy();
    }

    [Fact]
    public async Task CancellationAtTheEndOfTransferDoesNotReportFullCreationSuccess()
    {
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
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

        // Act
        var exitCode = await fixture.CreateShared(cancellation.Token);

        // Assert
        exitCode.Should().Be(130);
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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
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

        // Act
        var exitCode = await fixture.Replace(entry);

        // Assert
        exitCode.Should().Be(1);
        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().BeAfter(OldBuild);
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
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
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

        // Act
        var exitCode = await fixture.CreateShared(cancellation.Token);

        // Assert
        exitCode.Should().Be(130);
        fixture.Records.Load(fixture.Name)!.BuildCreatedAt.Should().BeAfter(OldBuild);
        await fixture.NoCopy();
        fixture
            .Console.AllText.Should()
            .Contain("record retained")
            .And.NotContain("Created sandbox");
    }

    [Fact]
    public async Task CancellationDuringCreationDoesNotStartASubsequentCopy()
    {
        // Arrange
        using var fixture = new UserSecretsLifecycleFixture();
        fixture.Configure("""{"squad":"ac","userSecrets":["sample"]}""");
        fixture.Store("sample", "{}");
        fixture
            .Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<SbxOutcome>(new OperationCanceledException("synthetic-private"))
            );

        // Act
        var exitCode = await fixture.Create();

        // Assert
        exitCode.Should().Be(130);
        await fixture.NoCopy();
        fixture.Records.Load(fixture.Name).Should().BeNull();
        fixture
            .Console.AllText.Should()
            .NotContain("synthetic-private")
            .And.NotContain("Created sandbox");
    }
}
