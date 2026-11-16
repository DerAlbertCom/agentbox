using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class AppPortsLifecycleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EmptyOrNonWebDotnetFoldersNeedNoCertificateCliOrPortRuntime(bool library)
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        if (library)
            fixture.Root.File("folder/Library.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        // Act
        var exit = await fixture.Create(false);

        // Assert
        exit.Should().Be(0, fixture.Console.AllText);
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
        fixture
            .Console.AllText.Should()
            .Contain("No explicit application ports")
            .And.Contain("Created sandbox");
    }

    [Theory]
    [InlineData("published", false)]
    [InlineData("local", false)]
    [InlineData("run", false)]
    [InlineData("published", true)]
    [InlineData("local", true)]
    [InlineData("run", true)]
    public async Task RequiredPublishingFailureRetainsRecordAndOffersRetryWithoutLaunching(
        string entry,
        bool cancel
    )
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        fixture.Existing();
        fixture.Angular();
        fixture
            .Sbx.EnsureApplicationPortsAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                Task.FromException(
                    cancel
                        ? new OperationCanceledException()
                        : new AboxException("synthetic port conflict")
                )
            );

        // Act
        var exit = await fixture.Replace(entry);

        // Assert
        exit.Should().Be(cancel ? 130 : 1, fixture.Console.AllText);
        fixture
            .Records.Load(fixture.Name)!
            .BuildCreatedAt.Should()
            .BeAfter(AppPortsLifecycleFixture.OldBuild);
        fixture
            .Console.AllText.Should()
            .Contain("record retained")
            .And.Contain("may remain")
            .And.Contain("abox ports --location \"" + fixture.Location + "\"")
            .And.NotContain("Created sandbox '")
            .And.NotContain("Open it with");
        await fixture
            .Sbx.DidNotReceive()
            .ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("published", "malformed")]
    [InlineData("local", "malformed")]
    [InlineData("run", "malformed")]
    [InlineData("published", "overflow")]
    [InlineData("local", "overflow")]
    [InlineData("run", "overflow")]
    public async Task InvalidDiscoveryCannotMutateOrReplaceTheExistingSandbox(
        string entry,
        string problem
    )
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        fixture.Existing();
        fixture.Angular();
        if (problem == "overflow")
            fixture.Configure("""{"portOffset":65534}""");
        else
            fixture.Root.File("folder/api/Properties/launchSettings.json", "{broken");

        // Act
        var exit = await fixture.Replace(entry);

        // Assert
        exit.Should().Be(1, fixture.Console.AllText);
        await fixture
            .Sbx.DidNotReceive()
            .RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        await fixture
            .Sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
        await fixture
            .Sbx.DidNotReceive()
            .EnsureKitSourceAllowedAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        fixture
            .Records.Load(fixture.Name)!
            .BuildCreatedAt.Should()
            .Be(AppPortsLifecycleFixture.OldBuild);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreationWithoutFolderConfigurationPublishesAngularPortsAfterSavingItsRecord(
        bool local
    )
    {
        // Arrange
        using var fixture = new AppPortsLifecycleFixture();
        fixture.Angular();
        fixture
            .Sbx.EnsureApplicationPortsAsync(
                fixture.Name,
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                fixture.Records.Load(fixture.Name)!.Squad.Should().Be("ac");
                fixture.Console.AllText.Should().NotContain("Created sandbox");
                return Task.CompletedTask;
            });

        // Act
        var exit = await fixture.Create(local);

        // Assert
        exit.Should().Be(0, fixture.Console.AllText);
        await fixture
            .Sbx.Received(1)
            .EnsureApplicationPortsAsync(
                fixture.Name,
                Arg.Is<IReadOnlyList<ApplicationPortMapping>>(ports =>
                    ports.Count == 1 && ports[0] == new ApplicationPortMapping(4200, 14200)
                ),
                Arg.Any<CancellationToken>()
            );
        await fixture
            .Sbx.DidNotReceive()
            .GetDevelopmentCertificatePasswordAsync(
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
        fixture.Console.AllText.Should().Contain("| 4200 | 14200").And.Contain("Created sandbox");
    }
}
