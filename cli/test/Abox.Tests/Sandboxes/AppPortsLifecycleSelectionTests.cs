using Abox.Naming;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class AppPortsLifecycleSelectionTests
{
    [Theory]
    [InlineData("published", false)]
    [InlineData("local", false)]
    [InlineData("run", false)]
    [InlineData("published", true)]
    [InlineData("local", true)]
    [InlineData("run", true)]
    public async Task ReplacementUsesTheEntryTimeOffsetIncludingAbsenceDespiteLaterFolderEdits(
        string entry,
        bool configured
    )
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        fixture.Existing();
        fixture.Angular();
        if (configured)
            fixture.Configure("""{"portOffset":20000}""");
        if (entry == "run")
            fixture
                .Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    fixture.Configure("""{"portOffset":30000}""");
                    return new[]
                    {
                        new SandboxInfo(fixture.Name, "running", AppPortsLifecycleFixture.OldBuild),
                    };
                });
        else
            fixture
                .Sbx.GetVersionAsync(Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    fixture.Configure("""{"portOffset":30000}""");
                    return new Version(0, 51, 0);
                });

        // Act
        var exit = await fixture.Replace(entry);

        // Assert
        exit.Should().Be(0, fixture.Console.AllText);
        fixture.Console.AllText.Should().Contain(configured ? "| 4200 | 24200" : "| 4200 | 14200");
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                fixture.Name,
                Arg.Is<IReadOnlyList<ApplicationPortMapping>>(ports =>
                    ports[0].HostPort == (configured ? 24200 : 14200)
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData("published")]
    [InlineData("local")]
    [InlineData("run")]
    public async Task ReplacementRetainsTheSelectedLegacyIdentityForRecordPortsAndLaunch(
        string entry
    )
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        fixture.Name = LegacySandboxName.For(fixture.Location, fixture.Location);
        fixture.Existing();
        fixture.Angular();
        fixture
            .Sbx.RemoveSandboxAsync(fixture.Name, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                fixture
                    .Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
                    .Returns(
                        new[] { new SandboxInfo("another-current-candidate", "running", null) }
                    );
                return new SbxOutcome(0, "");
            });

        // Act
        var exit = await fixture.Replace(entry);

        // Assert
        exit.Should().Be(0, fixture.Console.AllText);
        fixture.Records.Load(fixture.Name).Should().NotBeNull();
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                fixture.Name,
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request => request.Name == fixture.Name),
                Arg.Any<CancellationToken>()
            );
        if (entry == "run")
            await fixture
                .Sbx.Received(1)
                .ExecAsync(
                    Arg.Is<SbxExecRequest>(request => request.Sandbox == fixture.Name),
                    Arg.Any<CancellationToken>()
                );
    }

    [Theory]
    [InlineData("ordinary")]
    [InlineData("declined-run")]
    [InlineData("declined-create")]
    public async Task NonReplacingRunsAndDeclinedCreationNeverAcquireImplicitSetup(string entry)
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        fixture.Existing();
        fixture.Root.File(
            "folder/angular.json",
            "malformed input which must not be discovered by ordinary run"
        );
        fixture.Console.Answer = false;
        if (entry == "ordinary")
        {
            var record = fixture.Records.Load(fixture.Name)!;
            record.BuildCreatedAt = DateTimeOffset.MaxValue;
            fixture.Records.Save(fixture.Name, record);
        }

        // Act
        var exit = await (
            entry == "declined-create" ? fixture.Create(false) : fixture.Invoke("run")
        );

        // Assert
        exit.Should().Be(entry == "declined-create" ? 1 : 0, fixture.Console.AllText);
        await fixture
            .Sbx.DidNotReceive()
            .EnsureApplicationPortsAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Sbx.DidNotReceive()
            .GetDevelopmentCertificatePasswordAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
