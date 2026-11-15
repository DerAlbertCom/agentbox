using System.Text.Json;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using NSubstitute;
using Fixture = Abox.Tests.UserSecretsCliHarness;

namespace Abox.Tests;

public sealed class UserSecretsStorePatternTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MatchingDirectoryWithoutSecretsFileFailsAllPreparationRatherThanBeingANoMatch(
        bool force
    )
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"userSecrets":["Cape.*"]}""");
        fixture.Store("Cape.A", "{}");
        fixture.Root.Directory_("appdata/Microsoft/UserSecrets/Cape.Z");

        (await fixture.Run(force ? ["--force"] : [])).Should().Be(1);

        fixture
            .Console.AllText.Should()
            .Contain("Cape.Z")
            .And.Contain("existence")
            .And.NotContain("No user-secret stores selected");
        fixture.Sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task ExpansionRetainsDeclarationOrderWhenALaterOverlapUnionsAnEarlierStoresKeys()
    {
        using var fixture = new Fixture();
        fixture.Configure(
            """{"userSecrets":[{"id":"Zeta.*","keys":["A"]},"solo","Cape.*",{"id":"Zeta.A","keys":["B"]}]}"""
        );
        fixture.Store("Cape.A", "{}");
        fixture.Store("solo", "{}");
        fixture.Store("Zeta.Z", "{}");
        fixture.Store("Zeta.A", """{"A":1,"B":2}""");

        (await fixture.Run()).Should().Be(0, fixture.Console.AllText);

        var writes = Writes(fixture);
        writes.Select(write => write.Id).Should().Equal("Zeta.A", "Zeta.Z", "solo", "Cape.A");
        writes[0].Json.Should().Be("""{"A":1,"B":2}""");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LinkedProfileAncestorsAreAllowedForExactAndPrefixSelections(bool prefix)
    {
        using var fixture = new Fixture();
        using var profile = new TempDirectory();
        fixture.Configure(
            prefix ? """{"userSecrets":["Cape.*"]}""" : """{"userSecrets":["Cape.Deals"]}"""
        );
        var source = profile.File(
            "Microsoft/UserSecrets/Cape.Deals/secrets.json",
            """{"Password":"linked-profile-synthetic"}"""
        );
        var link = Path.Combine(fixture.Root.Path, "appdata");
        Directory.Delete(link);
        if (OperatingSystem.IsWindows())
        {
            var result = await new ProcessRunner().RunAsync(
                new ProcessRequest("cmd", ["/c", "mklink", "/J", link, profile.Path]),
                CancellationToken.None
            );
            result.ExitCode.Should().Be(0, result.StandardError);
        }
        else
            Directory.CreateSymbolicLink(link, profile.Path);

        try
        {
            (await fixture.Run()).Should().Be(0, fixture.Console.AllText);

            Writes(fixture).Select(write => write.Id).Should().Equal("Cape.Deals");
            fixture.Console.AllText.Should().NotContain("linked-profile-synthetic");
            File.ReadAllText(source).Should().Be("""{"Password":"linked-profile-synthetic"}""");
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Theory]
    [InlineData("allow")]
    [InlineData("deny")]
    [InlineData("malformed")]
    public async Task ExpandedStoresStillRequireValidEffectivePolicyAndNeverOverrideAnEffectiveDeny(
        string decision
    )
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"userSecrets":["Cape.*"]}""");
        fixture.Store("Cape.Deals", """{"Endpoint":"https://localhost:8004/synthetic-private"}""");
        var runner = Substitute.For<IProcessRunner>();
        var requests = new List<ProcessRequest>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                requests.Add(request);
                var json =
                    request.Arguments[1] == "ls"
                        ? $$"""
                            {"rules":[{"name":"kit:{{fixture.Name}}","scope":"sandbox:{{fixture.Name}}",
                            "applies_to":"sandbox:{{fixture.Name}}","resource_type":"network","decision":"allow",
                            "origin":"scoped","layer":"local","status":"active","editable":false,
                            "sandbox_id":"{{fixture.Name}}","provenance":{"created_via":"provisioned","source":"sandboxes"},
                            "actions":["net:connect:tcp"],"resources":["localhost:8004"]}]}
                            """
                    : decision == "malformed" ? "{}"
                    : $$"""
                        {"action":"net:connect:tcp","allowed":{{(
                            decision == "allow" ? "true" : "false"
                        )}},"context":"sandbox:{{fixture.Name}}",
                        "resource_type":"net:domain","resource_value":"localhost:8004","target":"localhost:8004","type":"network"}
                        """;
                return new ProcessResult(0, json, "");
            });
        var policyClient = new SbxClient(
            runner,
            AboxPaths.For(fixture.Root.Path, fixture.Root.Path)
        );
        fixture
            .Sbx.GetUserSecretsHostPortsAsync(fixture.Name, Arg.Any<CancellationToken>())
            .Returns(call =>
                policyClient.GetUserSecretsHostPortsAsync(
                    fixture.Name,
                    call.Arg<CancellationToken>()
                )
            );

        (await fixture.Run("--force", "--verbose"))
            .Should()
            .Be(decision == "malformed" ? 1 : 0, fixture.Console.AllText);

        requests.Select(request => request.Arguments[1]).Should().Equal("ls", "check");
        if (decision == "malformed")
            await fixture.NoWrites();
        else
        {
            var write = Writes(fixture).Should().ContainSingle().Subject;
            write.Id.Should().Be("Cape.Deals");
            write
                .Json.Should()
                .Contain(
                    decision == "allow"
                        ? "https://host.docker.internal:8004/"
                        : "https://localhost:8004/"
                );
        }
        fixture
            .Console.AllText.Should()
            .NotContain("synthetic-private")
            .And.NotContain("Safety override");
    }

    [Fact]
    public async Task MatchingCaseCollidingHostIdsFailRatherThanChoosingAStore()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"userSecrets":["Cape.*"]}""");
        fixture.Store("Cape.Deals", "{}");
        fixture.Store("cape.deals", "{}");
        if (
            Directory
                .GetDirectories(Path.Combine(fixture.Root.Path, "appdata/Microsoft/UserSecrets"))
                .Length != 2
        )
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value
                    + "This host filesystem cannot create case-distinct synthetic store directories."
            );

        (await fixture.Run("--force")).Should().Be(1);

        fixture.Console.AllText.Should().Contain("collide case-insensitively");
        fixture.Sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("invalid JSON")]
    [InlineData("{\"A\":1,\"a\":2}")]
    public async Task ExpandedHostStoresAreFullyValidatedBeforeAnySandboxAccessEvenWithForce(
        string json
    )
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"userSecrets":["Cape.*"]}""");
        fixture.Store("Cape.A", "{}");
        fixture.Store("Cape.Z", json);

        (await fixture.Run("--force")).Should().Be(1);

        fixture.Sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task OverlengthPrefixesFailBeforeHostEnumeration()
    {
        using var fixture = new Fixture();
        fixture.Configure(
            JsonSerializer.Serialize(new { userSecrets = new[] { new string('a', 129) + "*" } })
        );
        (await fixture.Run("--force")).Should().Be(1);
        fixture.Console.AllText.Should().Contain("Unsafe user-secret ID pattern");
        fixture.Sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task TerminalStarCanMatchAnEmptySuffixAtTheMaximumActualIdLength()
    {
        using var fixture = new Fixture();
        var id = new string('a', 128);
        fixture.Configure(JsonSerializer.Serialize(new { userSecrets = new[] { id + "*" } }));
        fixture.Store(id, "{}");

        (await fixture.Run()).Should().Be(0, fixture.Console.AllText);

        Writes(fixture).Select(write => write.Id).Should().Equal(id);
    }

    [Fact]
    public async Task NoMatchPrefixesDoNotMakeMissingExactIdsOptional()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"userSecrets":["Cape.*","missing"]}""");

        (await fixture.Run("--force")).Should().Be(1);

        fixture.Console.AllText.Should().Contain("missing").And.Contain("permissions");
        fixture.Sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task RepeatedCaseVariantPrefixSelectionsRemainConfigurationErrors()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"userSecrets":["Cape.*",{"id":"CAPE.*","keys":["A"]}]}""");

        (await fixture.Run("--force")).Should().Be(1);

        fixture.Console.AllText.Should().Contain("duplicate");
        fixture.Sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task OverlengthMatchingHostIdsFailStrictActualIdValidation()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"userSecrets":["Cape.*"]}""");
        fixture.Store("Cape." + new string('a', 124), "{}");

        (await fixture.Run("--force")).Should().Be(1);

        fixture.Console.AllText.Should().Contain("Unsafe user-secret ID");
        fixture.Sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrefixWithoutMatchesSelectsNoStoresEvenWhenTheHostRootIsAbsent(
        bool absentRoot
    )
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"userSecrets":["Cape.*"]}""");
        if (!absentRoot)
            fixture.Store("Other", "not selected JSON");

        (await fixture.Run()).Should().Be(0, fixture.Console.AllText);

        fixture.Console.AllText.Should().Contain("No user-secret stores selected");
        await fixture.NoWrites();
        await fixture
            .Sbx.DidNotReceive()
            .GetUserSecretsHostPortsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OverlappingIdsAndPrefixesUnionKeysAndCopyEachActualHostIdOnce(bool wholeStore)
    {
        using var fixture = new Fixture();
        fixture.Configure(
            wholeStore
                ? """{"userSecrets":["cape.deals",{"id":"CAPE.*","keys":["A"]}]}"""
                : """{"userSecrets":[{"id":"CAPE.*","keys":["A"]},{"id":"cape.deals","keys":["B"]},{"id":"Cape.D*","keys":["C"]}]}"""
        );
        fixture.Store("Cape.Deals", """{"A":1,"B":2,"C":3,"Unselected":4}""");
        fixture.Store("cape.position", """{"A":1,"B":2}""");

        (await fixture.Run()).Should().Be(0, fixture.Console.AllText);

        var writes = Writes(fixture);
        writes.Select(write => write.Id).Should().Equal("Cape.Deals", "cape.position");
        using var deals = JsonDocument.Parse(writes[0].Json);
        deals
            .RootElement.EnumerateObject()
            .Select(property => property.Name)
            .Should()
            .Equal(wholeStore ? ["A", "B", "C", "Unselected"] : ["A", "B", "C"]);
        writes[1].Json.Should().Be("""{"A":1}""");
    }

    [Theory]
    [InlineData("*")]
    [InlineData("Cape.**")]
    [InlineData("Ca*pe.*")]
    [InlineData("Cape.*suffix")]
    [InlineData("Cape?.*")]
    [InlineData("Cape.[ab]*")]
    [InlineData("../Cape.*")]
    [InlineData("Cape.*/child")]
    [InlineData("C:\\Cape.*")]
    [InlineData("/Cape.*")]
    [InlineData("Cape\\*")]
    [InlineData("CON.*")]
    [InlineData("..*")]
    [InlineData(" Cape.*")]
    [InlineData("Cape.\n*")]
    public async Task UnsafeOrBroaderStorePatternsFailBeforeSandboxAccessEvenWithForce(
        string pattern
    )
    {
        using var fixture = new Fixture();
        fixture.Configure(JsonSerializer.Serialize(new { userSecrets = new[] { pattern } }));

        (await fixture.Run("--force")).Should().Be(1);

        fixture.Console.AllText.Should().Contain("Unsafe user-secret ID pattern");
        fixture.Sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData("Cape.bad name")]
    [InlineData("Cape.ü")]
    public async Task MatchingUnsafeHostDirectoryNamesAreNotCopiedOrSilentlySkipped(string id)
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"userSecrets":["Cape.*"]}""");
        fixture.Store("Cape.A", "{}");
        fixture.Store(id, """{"Password":"unsafe-synthetic"}""");

        (await fixture.Run("--force")).Should().Be(1);

        fixture
            .Console.AllText.Should()
            .Contain("Unsafe user-secret ID")
            .And.NotContain("unsafe-synthetic");
        fixture.Sbx.ReceivedCalls().Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpandedIdsEnforceDenyListBeforeSandboxAccessAndForceOverridesOnlyMatchedValues(
        bool force
    )
    {
        using var fixture = new Fixture(new AboxConfig { UserSecretsDenyList = ["prod.example"] });
        fixture.Configure("""{"userSecrets":["Cape.*"]}""");
        fixture.Store("Cape.A", "{}");
        fixture.Store("Cape.Z", """{"Endpoint":"https://PROD.EXAMPLE/synthetic-private"}""");

        (await fixture.Run(force ? ["--force"] : []))
            .Should()
            .Be(force ? 0 : 1, fixture.Console.AllText);

        fixture
            .Console.AllText.Should()
            .NotContain("PROD.EXAMPLE")
            .And.NotContain("synthetic-private");
        if (force)
        {
            Writes(fixture).Select(write => write.Id).Should().Equal("Cape.A", "Cape.Z");
            fixture.Console.AllText.Should().Contain("Safety override");
        }
        else
        {
            fixture.Sbx.ReceivedCalls().Should().BeEmpty();
            fixture
                .Console.AllText.Should()
                .Contain("Cape.Z")
                .And.Contain("Endpoint")
                .And.Contain("--force");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PrefixEnumerationUsesTheDotNetHostRootAndNeverRecurses(bool unix)
    {
        using var fixture = new Fixture(unix: unix);
        fixture.Configure("""{"userSecrets":["Cape.*"]}""");
        fixture.Root.File(
            "home/.microsoft/usersecrets/Cape.Home/secrets.json",
            unix ? "{}" : "invalid unselected JSON"
        );
        fixture.Store("Cape.AppData", unix ? "invalid unselected JSON" : "{}");
        fixture.Root.File(
            "appdata/Microsoft/UserSecrets/Other/Cape.Nested/secrets.json",
            "invalid unselected JSON"
        );
        fixture.Store("Unrelated bad name", "invalid unselected JSON");

        (await fixture.Run()).Should().Be(0, fixture.Console.AllText);

        Writes(fixture)
            .Select(write => write.Id)
            .Should()
            .Equal(unix ? "Cape.Home" : "Cape.AppData");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task HostDirectoryLinksCannotRedirectExactOrPrefixSelections(
        bool rootLink,
        bool prefix
    )
    {
        using var fixture = new Fixture();
        using var outside = new TempDirectory();
        fixture.Configure(
            prefix ? """{"userSecrets":["Cape.*"]}""" : """{"userSecrets":["Cape.Deals"]}"""
        );
        var target = rootLink ? outside.Directory_("stores") : outside.Directory_("store");
        var source = outside.File(
            rootLink ? "stores/Cape.Deals/secrets.json" : "store/secrets.json",
            """{"Password":"outside-synthetic"}"""
        );
        var link = Path.GetFullPath(
            Path.Combine(fixture.Root.Directory_("appdata/Microsoft"), "UserSecrets")
        );
        if (!rootLink)
        {
            Directory.CreateDirectory(link);
            link = Path.Combine(link, "Cape.Deals");
        }
        if (OperatingSystem.IsWindows())
        {
            var result = await new ProcessRunner().RunAsync(
                new ProcessRequest("cmd", ["/c", "mklink", "/J", link, target]),
                CancellationToken.None
            );
            result.ExitCode.Should().Be(0, result.StandardError);
        }
        else
            Directory.CreateSymbolicLink(link, target);

        try
        {
            (await fixture.Run("--force")).Should().Be(1, fixture.Console.AllText);

            fixture
                .Console.AllText.Should()
                .Contain("symbolic link")
                .And.NotContain("outside-synthetic");
            await fixture.NoWrites();
            File.ReadAllText(source).Should().Be("""{"Password":"outside-synthetic"}""");
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Fact]
    public async Task CapePrefixCopiesEveryMatchingHostStoreIntoTheExistingSandbox()
    {
        using var fixture = new Fixture();
        fixture.Configure("""{"squad":"ac","userSecrets":["Cape.*"]}""");
        fixture.Store("Cape.Position", """{"Selected":"position-synthetic"}""");
        fixture.Store("Cape.Deals", """{"Selected":"deals-synthetic"}""");
        fixture.Store("CapeOther", "not selected JSON");
        fixture.Store("Other.Cape.Deals", "not selected JSON");

        (await fixture.Run()).Should().Be(0, fixture.Console.AllText);

        var writes = fixture
            .Sbx.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ISbxClient.WriteUserSecretsAsync))
            .Select(call => (string)call.GetArguments()[1]!);
        writes.Should().Equal("Cape.Deals", "Cape.Position");
        fixture
            .Console.AllText.Should()
            .NotContain("position-synthetic")
            .And.NotContain("deals-synthetic");
        await fixture
            .Sbx.DidNotReceive()
            .CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
    }

    private static (string Id, string Json)[] Writes(Fixture fixture) =>
        fixture
            .Sbx.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ISbxClient.WriteUserSecretsAsync))
            .Select(call => ((string)call.GetArguments()[1]!, (string)call.GetArguments()[2]!))
            .ToArray();
}
