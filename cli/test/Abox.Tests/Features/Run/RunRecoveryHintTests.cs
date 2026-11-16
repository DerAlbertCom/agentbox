using Abox.Configuration;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class RunRecoveryHintTests
{
    [Fact]
    public async Task MissingProgramUsesTheSelectedLegacySandboxesRecordNotTheCurrentNamesRecord()
    {
        // Arrange
        using var fixture = new HintFixture();
        var legacy =
            "folder-ai-sbx-"
            + Convert.ToHexStringLower(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(fixture.Cli.Location)
                )
            )[..6];
        fixture.Record(
            new SandboxRecord
            {
                Team = "unrelated",
                Squad = "wrong",
                Tag = "wrong",
            }
        );
        fixture
            .Cli.Provider.GetRequiredService<SandboxRecordStore>()
            .Save(
                legacy,
                new SandboxRecord
                {
                    Team = "legacy-team",
                    Squad = "ac",
                    Tag = "release",
                    KitReference = "published-set",
                }
            );
        fixture
            .Cli.Sbx.FindSandboxAsync(legacy, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(legacy, "running", null));

        // Act
        var exitCode = await fixture.Run(true);

        // Assert
        exitCode.Should().Be(1);
        fixture
            .Cli.Console.AllText.Should()
            .Contain("--team legacy-team --squad ac --tag release")
            .And.NotContain("unrelated")
            .And.NotContain("--local");
        await fixture
            .Cli.Sbx.Received(1)
            .HasProgramAsync(legacy, "hc", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnprovenBuildReportsRebuildBeforeTheNonlocalRecoveryCommand(
        bool missingProgram
    )
    {
        // Arrange
        using var fixture = new HintFixture();
        fixture.Record(
            new SandboxRecord
            {
                Team = "recorded",
                Squad = "ac",
                Tag = "release",
            }
        );
        fixture.Cli.Root.File(
            "builds/recorded/ac/release/build.json",
            """{"Squad":"ac","Tag":"release","Reference":"agentbox-rwest-unrelated:release","RootDirectory":"unrelated-source-root"}"""
        );
        fixture.Missing(missingProgram);

        // Act
        var exitCode = await fixture.Run(missingProgram);

        // Assert
        exitCode.Should().Be(1);
        var output = fixture.Cli.Console.AllText;
        output
            .Should()
            .Contain("abox build --team recorded --tag release")
            .And.Contain("abox create --team recorded --squad ac --tag release --location")
            .And.NotContain("--local")
            .And.NotContain("--root")
            .And.NotContain("unrelated-source-root");
        output
            .IndexOf("abox build", StringComparison.Ordinal)
            .Should()
            .BeLessThan(output.IndexOf("abox create", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PublishedRecoveryNeedsNeitherSourcesNorReadableLocalBuilds(
        bool missingProgram
    )
    {
        // Arrange
        using var fixture = new HintFixture();
        fixture.Record(
            new SandboxRecord
            {
                Team = "published-team",
                Squad = "ac",
                Tag = "release",
                KitReference = "published-set",
            }
        );
        fixture.Cli.Root.File(
            "builds/published-team/ac/release/build.json",
            "invalid unrelated JSON"
        );
        fixture.Missing(missingProgram);

        // Act
        var exitCode = await fixture.Run(missingProgram);

        // Assert
        exitCode.Should().Be(1);
        fixture
            .Cli.Console.AllText.Should()
            .Contain(
                $"abox create --team published-team --squad ac --tag release --location \"{fixture.Cli.Location}\""
            )
            .And.NotContain("--local")
            .And.NotContain("--root")
            .And.NotContain("abox build");
    }

    [Theory]
    [InlineData("owned-legacy")]
    [InlineData("unowned-legacy")]
    [InlineData("qualified-only")]
    public async Task TeamlessRecordsRecoverLocalIdentityOnlyFromExplicitLegacyOwnership(
        string storage
    )
    {
        // Arrange
        using var fixture = new HintFixture();
        fixture.Record(new SandboxRecord { Squad = "ac", Tag = "release" });
        var manifest = new BuildManifest
        {
            Squad = "ac",
            Tag = "release",
            RootDirectory = "legacy sources",
            Reference = storage == "unowned-legacy" ? null : "agentbox-rwest-owned:release",
        };
        if (storage == "qualified-only")
            fixture.Builds.Save(manifest);
        else
            fixture.Cli.Root.File(
                "builds/ac/release/build.json",
                System.Text.Json.JsonSerializer.Serialize(manifest)
            );

        // Act
        var exitCode = await fixture.Run(false);

        // Assert
        exitCode.Should().Be(1);
        if (storage == "owned-legacy")
            fixture
                .Cli.Console.AllText.Should()
                .Contain("--team owned --squad ac --tag release --local --root \"legacy sources\"");
        else
            fixture
                .Cli.Console.AllText.Should()
                .Contain("--team default-team --squad ac --tag release")
                .And.NotContain("--local")
                .And.NotContain("legacy sources");
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("single")]
    [InlineData("multiple")]
    [InlineData("invalid")]
    public async Task OptionalLocalDiscoveryUsesOnlyAnUnambiguousSquadWithoutChangingDefaults(
        string sources
    )
    {
        // Arrange
        using var fixture = new HintFixture();
        if (sources != "absent")
            fixture.Cli.Root.File(
                "teams/default-team/ac.abox",
                sources == "invalid" ? "unknown: value" : "workload: sample"
            );
        if (sources == "multiple")
            fixture.Cli.Root.File("teams/default-team/other.abox", "workload: sample");

        // Act
        var exitCode = await fixture.Run(false);

        // Assert
        exitCode.Should().Be(1);
        fixture
            .Cli.Console.AllText.Should()
            .Contain(
                sources == "single"
                    ? "--team default-team --squad ac --tag default-tag"
                    : "--team default-team --squad <name> --tag default-tag"
            );
        fixture.Cli.Console.AllText.Should().NotContain("--local").And.NotContain("--root");
    }

    [Theory]
    [InlineData("{ broken JSON")]
    [InlineData(
        "{\"Squad\":\"ac\",\"Tag\":\"release\",\"Reference\":\"agentbox-rwest-unrelated:release\"}"
    )]
    public async Task InvalidLocalMetadataStillAllowsOrdinaryLaunch(string metadata)
    {
        // Arrange
        using var fixture = new HintFixture();
        fixture.Record(
            new SandboxRecord
            {
                Team = "recorded",
                Squad = "ac",
                Tag = "release",
            }
        );
        fixture.Cli.Root.File("builds/recorded/ac/release/build.json", metadata);
        fixture.Cli.ExistingSandbox();
        fixture
            .Cli.Sbx.ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>())
            .Returns(23);

        // Act
        var exitCode = await fixture.Run(false);

        // Assert
        exitCode.Should().Be(23);
        fixture
            .Cli.Console.AllText.Should()
            .Contain("abox build --team recorded --tag release")
            .And.NotContain("Replace the sandbox")
            .And.NotContain("abox create");
        await fixture
            .Cli.Sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FolderSquadWinsWhileTheRecordKeepsItsTeamTagAndOwnedSourceRoot(
        bool missingProgram
    )
    {
        // Arrange
        using var fixture = new HintFixture();
        fixture.Cli.Configure("""{"squad":"selected"}""");
        fixture.Record(
            new SandboxRecord
            {
                Team = "recorded",
                Squad = "old",
                Tag = "release",
            }
        );
        var sourceRoot = fixture.Cli.Root.Directory_("separate sources");
        fixture.Builds.Save(
            new BuildManifest
            {
                Squad = "selected",
                Tag = "release",
                Reference = "agentbox-rwest-recorded:release",
                RootDirectory = sourceRoot,
            }
        );
        fixture.Missing(missingProgram);

        // Act
        var exitCode = await fixture.Run(missingProgram);

        // Assert
        exitCode.Should().Be(1);
        fixture
            .Cli.Console.AllText.Should()
            .Contain(
                $"abox create --team recorded --squad selected --tag release --local --root \"{sourceRoot}\" --location \"{fixture.Cli.Location}\""
            );
        await fixture
            .Cli.Sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
    }

    private sealed class HintFixture : IDisposable
    {
        public UserSecretsCliHarness Cli { get; } =
            new(
                new AboxConfig { DefaultTeam = "default-team", DefaultTag = "default-tag" },
                existingSandbox: false
            );
        public BuildStore Builds => Cli.Provider.GetRequiredService<BuildStore>();

        public void Record(SandboxRecord record) =>
            Cli.Provider.GetRequiredService<SandboxRecordStore>().Save(Cli.Name, record);

        public void Missing(bool program)
        {
            if (program)
                Cli.ExistingSandbox();
            Cli.Sbx.HasProgramAsync(
                    Arg.Any<string>(),
                    Arg.Any<string>(),
                    Arg.Any<CancellationToken>()
                )
                .Returns(false);
        }

        public Task<int> Run(bool missingProgram) =>
            Cli.Invoke("run", missingProgram ? "herdr-app" : "shell", "--location", Cli.Location);

        public void Dispose() => Cli.Dispose();
    }
}
