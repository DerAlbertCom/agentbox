using System.Text.Json;
using Abox.Configuration;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsOracleCommandTests
{
    [Theory]
    [InlineData("//127.0.0.2:1521/synthetic-service", true)]
    [InlineData("//host.docker.internal:1521/synthetic-service", true)]
    [InlineData("//remote.test:1521/synthetic-service", false)]
    [InlineData("//localhost:5000/synthetic-service", false)]
    [InlineData("//localhost:1521/synthetic-instance", false)]
    [InlineData("//localhost:1521/synthetic-service?retry_count=3", false)]
    public async Task OracleOverridesUseSharedStoreCompatibilityIncludingUnchangedConsumers(
        string second,
        bool compatible
    )
    {
        foreach (var reverse in new[] { false, true })
        {
            using var cli = new UserSecretsCliHarness();
            cli.Configure("""{"userSecrets":["first","sample"]}""");
            cli.Store("first", "{}");
            foreach (
                var (name, endpoint) in new[]
                {
                    (reverse ? "Worker" : "Api", "//localhost:1521/synthetic-service"),
                    (reverse ? "Api" : "Worker", second),
                }
            )
            {
                cli.Root.File(
                    $"folder/{name}/{name}.csproj",
                    "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
                );
                cli.Root.File(
                    $"folder/{name}/appsettings.json",
                    JsonSerializer.Serialize(new { Oracle = endpoint })
                );
            }
            MappedPorts(cli);
            var originals = Originals(cli);

            (await cli.Run("--verbose")).Should().Be(compatible ? 0 : 1, cli.Console.AllText);

            if (compatible)
            {
                using var written = JsonDocument.Parse(Written(cli, "sample"));
                written.RootElement.EnumerateObject().Should().ContainSingle();
                written
                    .RootElement.GetProperty("Oracle")
                    .GetString()
                    .Should()
                    .Be("//host.docker.internal:1521/synthetic-service");
            }
            else
            {
                await cli.NoWrites();
                cli.Console.AllText.Should()
                    .Contain("conflict")
                    .And.Contain("Api.csproj")
                    .And.Contain("Worker.csproj");
            }
            SafeDiagnostics(cli);
            Unchanged(originals);
        }
    }

    [Fact]
    public async Task SharedOracleDescriptorsMergeAndAuthoritativeSecretsSuppressLowerPriorityConflicts()
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        const string descriptor =
            "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SID=synthetic-service)))";
        Project(cli, new { Oracle = descriptor });
        cli.Root.File(
            "folder/Worker/Worker.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            "folder/Worker/appsettings.json",
            JsonSerializer.Serialize(
                new
                {
                    Oracle = "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=::1)(PORT=1521))(CONNECT_DATA=(SID=synthetic-service)))",
                }
            )
        );
        MappedPorts(cli);

        (await cli.Run()).Should().Be(0, cli.Console.AllText);
        using (var written = JsonDocument.Parse(Written(cli, "sample")))
            written
                .RootElement.GetProperty("Oracle")
                .GetString()
                .Should()
                .Be(
                    "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=host.docker.internal)(PORT=1521))(CONNECT_DATA=(SID=synthetic-service)))"
                );

        cli.Root.File(
            "folder/Worker/appsettings.json",
            """{"Oracle":"//remote.test:1521/synthetic-instance"}"""
        );
        cli.Store("sample", JsonSerializer.Serialize(new { Oracle = descriptor }));
        (await cli.Run()).Should().Be(0, cli.Console.AllText);
        using (var written = JsonDocument.Parse(Written(cli, "sample")))
            written
                .RootElement.GetProperty("Oracle")
                .GetString()
                .Should()
                .Be(
                    "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=host.docker.internal)(PORT=1521))(CONNECT_DATA=(SID=synthetic-service)))"
                );
        SafeDiagnostics(cli);
    }

    [Theory]
    [InlineData("\"sample\"", 5)]
    [InlineData("{\"id\":\"sample\",\"keys\":[\"oracle:*\"]}", 4)]
    [InlineData("{\"id\":\"sample\",\"keys\":[\"Oracle:Primary\"]}", 1)]
    [InlineData("{\"id\":\"sample\",\"keys\":[]}", 0)]
    public async Task DevelopmentAndAuthoritativeSecretsWinBeforeOracleEligibilityAndFiltering(
        string selection,
        int expectedCount
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("{\"userSecrets\":[" + selection + "]}");
        Project(
            cli,
            new
            {
                Oracle = new
                {
                    Primary = "localhost:1521/base",
                    Generated = "localhost:1521/base",
                    Remote = "localhost:1521/base",
                    Null = "localhost:1521/base",
                },
                Other = "localhost:1521/base",
            }
        );
        cli.Root.File(
            "folder/Api/appsettings.development.json",
            """{"oracle":{"primary":"localhost:1521/development","generated":"//[::1]:1521/synthetic-service","remote":"localhost:1521/development"},"Other":"localhost:1521/development"}"""
        );
        cli.Store(
            "sample",
            """{"ORACLE:Primary":"Data Source=localhost:1521/authoritative;User Id=synthetic-user","Oracle:Remote":"remote.test:1521/synthetic-service","Oracle:Null":null}"""
        );
        MappedPorts(cli);
        var originals = Originals(cli);

        (await cli.Run("--verbose")).Should().Be(0, cli.Console.AllText);

        using var written = JsonDocument.Parse(Written(cli, "sample"));
        written.RootElement.EnumerateObject().Should().HaveCount(expectedCount);
        if (expectedCount > 0)
            written
                .RootElement.GetProperty("ORACLE:Primary")
                .GetString()
                .Should()
                .Be("Data Source=host.docker.internal:1521/authoritative;User Id=synthetic-user");
        if (expectedCount > 1)
        {
            written
                .RootElement.GetProperty("oracle:generated")
                .GetString()
                .Should()
                .Be("//host.docker.internal:1521/synthetic-service");
            written
                .RootElement.GetProperty("Oracle:Remote")
                .GetString()
                .Should()
                .Be("remote.test:1521/synthetic-service");
            written
                .RootElement.GetProperty("Oracle:Null")
                .ValueKind.Should()
                .Be(JsonValueKind.Null);
        }
        if (expectedCount == 5)
            written
                .RootElement.GetProperty("Other")
                .GetString()
                .Should()
                .Be("host.docker.internal:1521/development");
        SafeDiagnostics(cli);
        Unchanged(originals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AuthoritativeOrExcludedKeysSuppressEvenUnsupportedLowerPriorityOracleValues(
        bool authoritative
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure(
            authoritative
                ? """{"userSecrets":["sample"]}"""
                : """{"userSecrets":[{"id":"sample","keys":[]}]}"""
        );
        Project(
            cli,
            new
            {
                Endpoint = "Data Source=localhost:1521/synthetic-service;Data Source=remote.test:1521/ambiguous",
            }
        );
        if (authoritative)
            cli.Store("sample", """{"endpoint":"remote.test:1521/synthetic-service"}""");
        MappedPorts(cli);

        (await cli.Run("--verbose")).Should().Be(0, cli.Console.AllText);

        Written(cli, "sample")
            .Should()
            .Be(authoritative ? """{"endpoint":"remote.test:1521/synthetic-service"}""" : "{}");
        SafeDiagnostics(cli);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OracleOriginalsUseExistingDenyPolicyAndSafeForceAccounting(
        bool copied,
        bool force
    )
    {
        using var cli = new UserSecretsCliHarness(
            new AboxConfig { UserSecretsDenyList = ["synthetic-service"] }
        );
        cli.Configure("""{"userSecrets":["first","sample"]}""");
        cli.Store("first", "{}");
        const string endpoint =
            "Data Source=localhost:1521/synthetic-service;User Id=synthetic-user;Password='synthetic;password'";
        if (copied)
            cli.Store("sample", JsonSerializer.Serialize(new { Endpoint = endpoint }));
        else
            Project(cli, new { Endpoint = endpoint });
        MappedPorts(cli);
        var originals = Originals(cli);

        (await cli.Run(["--verbose", .. force ? new[] { "--force" } : []]))
            .Should()
            .Be(force ? 0 : 1, cli.Console.AllText);

        if (force)
        {
            using var written = JsonDocument.Parse(Written(cli, "sample"));
            written
                .RootElement.GetProperty("Endpoint")
                .GetString()
                .Should()
                .Be(
                    "Data Source=host.docker.internal:1521/synthetic-service;User Id=synthetic-user;Password='synthetic;password'"
                );
            cli.Console.AllText.Should().Contain("Safety override");
        }
        else
        {
            await cli.NoWrites();
            cli.Console.AllText.Should()
                .Contain("deny list")
                .And.Contain("sample")
                .And.Contain("Endpoint");
        }
        SafeDiagnostics(cli);
        Unchanged(originals);
    }

    [Fact]
    public async Task EachCopyUsesCurrentOracleConfigurationAndPolicyWithoutSelectingUnrequestedStores()
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        Project(cli, new { Endpoint = "localhost:1521/synthetic-service" });
        cli.Root.File(
            "folder/Unselected/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>unselected</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File(
            "folder/Unselected/appsettings.json",
            "not JSON: never inspect unselected sources"
        );
        MappedPorts(cli);

        (await cli.Run()).Should().Be(0, cli.Console.AllText);
        Written(cli, "sample").Should().Contain("host.docker.internal");

        cli.Root.File(
            "folder/Api/appsettings.json",
            """{"Endpoint":"localhost:5000/synthetic-service"}"""
        );
        (await cli.Run()).Should().Be(0, cli.Console.AllText);
        Written(cli, "sample").Should().Be("{}");

        Project(cli, new { Endpoint = "localhost:1521/synthetic-service" });
        cli.Store("sample", """{"Copied":"//localhost/synthetic-service"}""");
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<int>());
        (await cli.Run()).Should().Be(0, cli.Console.AllText);
        Written(cli, "sample").Should().Be("""{"Copied":"//localhost/synthetic-service"}""");
        SafeDiagnostics(cli);
    }

    [Theory]
    [InlineData("localhost:1521 is the local db")]
    [InlineData("localhost:8080 (worker)")]
    [InlineData("localhost:1521 is the local db/synthetic-service")]
    public async Task WhitespaceProseIsNotAnOracleEndpoint(string original)
    {
        foreach (var verbose in new[] { false, true })
        {
            using var cli = new UserSecretsCliHarness();
            cli.Configure("""{"userSecrets":["sample"]}""");
            Project(cli, new { Generated = original });
            cli.Store("sample", JsonSerializer.Serialize(new { Copied = original }));
            MappedPorts(cli);
            var originals = Originals(cli);

            (await cli.Run(verbose ? ["--verbose"] : [])).Should().Be(0, cli.Console.AllText);

            using var written = JsonDocument.Parse(Written(cli, "sample"));
            written.RootElement.EnumerateObject().Should().ContainSingle();
            written.RootElement.GetProperty("Copied").GetString().Should().Be(original);
            cli.Console.AllText.Should().NotContain(original);
            SafeDiagnostics(cli);
            Unchanged(originals);
        }
    }

    [Theory]
    [InlineData("remote.test:1521/synthetic-service")]
    [InlineData("//localhost:5000/synthetic-service")]
    [InlineData("//[::1]:5000/synthetic-service")]
    [InlineData("localhost,remote.test:5000/synthetic-service")]
    [InlineData("//localhost:5000,remote.test:1521/synthetic-service")]
    [InlineData("Data Source=//127.0.0.2:5000/synthetic-service;Password='synthetic;password'")]
    [InlineData(
        "(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=5000))(ADDRESS=(PROTOCOL=TCP)(HOST=remote.test)(PORT=1521)))(CONNECT_DATA=(SERVICE_NAME=synthetic-service)))"
    )]
    [InlineData(
        "(DESCRIPTION=(ADDRESS=(PROTOCOL=IPC)(KEY=localhost:1521))(CONNECT_DATA=(SERVICE_NAME=synthetic-service)))"
    )]
    [InlineData("a note about //localhost:1521/synthetic-service")]
    [InlineData("Password='Data Source=localhost:1521/synthetic-service';User Id=synthetic-user")]
    [InlineData(
        "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=remote.test))(CONNECT_DATA=(SERVICE_NAME=XE)))"
    )]
    [InlineData(
        "Data Source=\"(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=5000))(ADDRESS=(PROTOCOL=TCPS)(HOST=remote.test))))\""
    )]
    [InlineData(
        "Data Source=synthetic-alias;Password='(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521)))'"
    )]
    [InlineData(
        "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=remote.test)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=localhost:1521)))"
    )]
    [InlineData(
        "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=5000))) malformed synthetic-service"
    )]
    public async Task RemoteUnmappedAndNonEndpointValuesArePreservedWithoutGeneration(
        string original
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        Project(cli, new { Generated = original });
        cli.Store("sample", JsonSerializer.Serialize(new { Copied = original }));
        MappedPorts(cli);
        var originals = Originals(cli);

        (await cli.Run("--verbose")).Should().Be(0, cli.Console.AllText);

        using var written = JsonDocument.Parse(Written(cli, "sample"));
        written.RootElement.EnumerateObject().Should().ContainSingle();
        written.RootElement.GetProperty("Copied").GetString().Should().Be(original);
        SafeDiagnostics(cli);
        Unchanged(originals);
    }

    [Theory]
    [InlineData(
        "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost))(CONNECT_DATA=(SERVICE_NAME=XE)))"
    )]
    [InlineData(
        "Data Source=\"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=\"\"localhost\"\"))(CONNECT_DATA=(SERVICE_NAME=XE)))\";Password='synthetic;password'"
    )]
    [InlineData(
        "(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCP)(HOST=remote.test)(PORT=8004))(ADDRESS=(PROTOCOL=TCPS)(HOST=::1))))"
    )]
    [InlineData("(ADDRESS=(PROTOCOL=UDP)(HOST=localhost))")]
    [InlineData("Data Source=localhost:1521 is the local db")]
    [InlineData("//localhost:1521 is the local db")]
    [InlineData("localhost:1521/synthetic-service bad")]
    [InlineData("//localhost:1521/synthetic-service?retry_count='unterminated")]
    [InlineData("//localhost:1521,,remote.test:5000/synthetic-service")]
    [InlineData("//::1:1521/synthetic-service")]
    [InlineData("//0:0:0:0:0:0:0:1:1521/synthetic-service")]
    [InlineData("//localhost:1521/synthetic-service?host=remote.test")]
    [InlineData("//localhost:1521/synthetic-service?https_proxy=remote.test&https_proxy_port=443")]
    [InlineData(
        "Data Source=localhost:1521/synthetic-service;Data Source=remote.test:1521/synthetic-service"
    )]
    [InlineData("Data Source=localhost:1521/synthetic-service;Host=remote.test;Port=1521")]
    [InlineData("Data Source=localhost:1521/synthetic-service;Password='synthetic;password")]
    [InlineData("Data Source='localhost:1521/synthetic-service;User Id=synthetic-user")]
    [InlineData(
        "User Id=synthetic-user;Data Source=\"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SID=synthetic-service)))"
    )]
    [InlineData(
        "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(HOST=remote.test)(PORT=1521)))"
    )]
    [InlineData("(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=5000)(PORT=1521)))")]
    [InlineData("(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(PROTOCOL=TCPS)(HOST=::1)(PORT=1521)))")]
    [InlineData("(DESCRIPTION=(ADDRESS=(PROTOCOL=UDP)(HOST=localhost)(PORT=1521)))")]
    [InlineData("(DESCRIPTION=(ADDRESS=(HOST=localhost)(PORT=1521)))")]
    [InlineData(
        "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=synthetic-service))"
    )]
    [InlineData(
        "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))) trailing synthetic-service"
    )]
    [InlineData(
        "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCPS)(HOST=localhost)(PORT=1521)(HTTPS_PROXY=remote.test)(HTTPS_PROXY_PORT=443)))"
    )]
    public async Task UnsafeMappedOracleEndpointsFailBeforeAnyWriteWithoutLeakingValues(
        string endpoint
    )
    {
        foreach (var generated in new[] { false, true })
        foreach (var verbose in new[] { false, true })
        {
            using var cli = new UserSecretsCliHarness();
            cli.Configure("""{"userSecrets":["first","sample"]}""");
            cli.Store("first", """{"Valid":"http://localhost:8004/synthetic-service"}""");
            if (generated)
                Project(cli, new { Endpoint = endpoint });
            else
                cli.Store("sample", JsonSerializer.Serialize(new { Endpoint = endpoint }));
            MappedPorts(cli);
            var originals = Originals(cli);

            (await cli.Run(["--force", .. verbose ? new[] { "--verbose" } : []]))
                .Should()
                .Be(1, cli.Console.AllText);

            await cli.NoWrites();
            cli.Console.AllText.Should()
                .Contain("sample")
                .And.Contain("Endpoint")
                .And.Contain("mapped")
                .And.Contain("unsupported")
                .And.Contain("no stores were copied");
            if (generated)
                cli.Console.AllText.Should().Contain("Api.csproj");
            SafeDiagnostics(cli);
            Unchanged(originals);
        }
    }

    [Theory]
    [InlineData("(ADDRESS=(PROTOCOL=TCP)(HOST=localhost))")]
    [InlineData(
        "Data Source=\"(DESCRIPTION=(ADDRESS_LIST=(ADDRESS=(PROTOCOL=TCPS)(HOST=::1))(ADDRESS=(PROTOCOL=TCP)(HOST=remote.test)(PORT=8004))))\""
    )]
    public async Task DescriptorsWithoutPortArePreservedWhenDefaultIsUnmapped(string original)
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        Project(cli, new { Generated = original });
        cli.Store("sample", JsonSerializer.Serialize(new { Copied = original }));
        cli.Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 8004 });
        var originals = Originals(cli);

        (await cli.Run("--verbose")).Should().Be(0, cli.Console.AllText);

        using var written = JsonDocument.Parse(Written(cli, "sample"));
        written.RootElement.EnumerateObject().Should().ContainSingle();
        written.RootElement.GetProperty("Copied").GetString().Should().Be(original);
        SafeDiagnostics(cli);
        Unchanged(originals);
    }

    [Theory]
    [InlineData(
        "Data Source=\"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=\"\"localhost\"\")(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=synthetic-service)))\";Password=\"synthetic\"\"password\"",
        "Data Source=\"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=\"\"host.docker.internal\"\")(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=synthetic-service)))\";Password=\"synthetic\"\"password\""
    )]
    [InlineData(
        "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=synthetic-service)))",
        "(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=host.docker.internal)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=synthetic-service)))"
    )]
    [InlineData(
        " ( description = (address_list=(LOAD_BALANCE=ON)(address=(port=01521)(host=\"127.0.0.2\")(protocol=tcps))(address=(protocol=tcp)(host=remote.test)(port=1521))(address=(protocol=tcp)(host=localhost)(port=5000)))(connect_data=(sid=synthetic-service)(server=dedicated))(retry_count=3)) ",
        " ( description = (address_list=(LOAD_BALANCE=ON)(address=(port=01521)(host=\"host.docker.internal\")(protocol=tcps))(address=(protocol=tcp)(host=remote.test)(port=1521))(address=(protocol=tcp)(host=localhost)(port=5000)))(connect_data=(sid=synthetic-service)(server=dedicated))(retry_count=3)) "
    )]
    [InlineData(
        "(DESCRIPTION_LIST=(FAILOVER=ON)(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=::1)(PORT=1521))(CONNECT_DATA=(SID=synthetic-service)))(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=[0:0:0:0:0:0:0:1])(PORT=8004))(CONNECT_DATA=(SERVICE_NAME=synthetic-instance))))",
        "(DESCRIPTION_LIST=(FAILOVER=ON)(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=host.docker.internal)(PORT=1521))(CONNECT_DATA=(SID=synthetic-service)))(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=host.docker.internal)(PORT=8004))(CONNECT_DATA=(SERVICE_NAME=synthetic-instance))))"
    )]
    [InlineData(
        "User Id=synthetic-user; Password='synthetic;password'; Data Source=\"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=localhost)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=synthetic-service))(SECURITY=(SSL_SERVER_CERT_DN='CN=synthetic-wallet,OU=localhost:1521')))\"; Pooling=false",
        "User Id=synthetic-user; Password='synthetic;password'; Data Source=\"(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=host.docker.internal)(PORT=1521))(CONNECT_DATA=(SERVICE_NAME=synthetic-service))(SECURITY=(SSL_SERVER_CERT_DN='CN=synthetic-wallet,OU=localhost:1521')))\"; Pooling=false"
    )]
    public async Task DescriptorsPreserveStructureAndAdaptOnlyMappedAddressHosts(
        string original,
        string expected
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        Project(cli, new { Generated = original });
        cli.Store("sample", JsonSerializer.Serialize(new { Copied = original }));
        MappedPorts(cli);
        var originals = Originals(cli);

        (await cli.Run("--verbose")).Should().Be(0, cli.Console.AllText);

        using var written = JsonDocument.Parse(Written(cli, "sample"));
        written.RootElement.GetProperty("Generated").GetString().Should().Be(expected);
        written.RootElement.GetProperty("Copied").GetString().Should().Be(expected);
        SafeDiagnostics(cli);
        Unchanged(originals);
    }

    [Theory]
    [InlineData("localhost:1521", "host.docker.internal:1521")]
    [InlineData(
        "Data Source=localhost:8004/database",
        "Data Source=host.docker.internal:8004/database"
    )]
    [InlineData(
        "//localhost,remote.test:1521,127.0.0.2:5000,[::1]:8004/synthetic-service?load_balance=on",
        "//host.docker.internal,remote.test:1521,127.0.0.2:5000,host.docker.internal:8004/synthetic-service?load_balance=on"
    )]
    [InlineData(
        "tcps://remote.test:1521;localhost,[::1]:8004/synthetic-service?ssl_server_cert_dn=\"CN=synthetic-wallet,OU=localhost:1521\"&retry_count=3",
        "tcps://remote.test:1521;host.docker.internal,host.docker.internal:8004/synthetic-service?ssl_server_cert_dn=\"CN=synthetic-wallet,OU=localhost:1521\"&retry_count=3"
    )]
    [InlineData("//localhost/synthetic-service", "//host.docker.internal/synthetic-service")]
    [InlineData("localhost:1521/synthetic-service", "host.docker.internal:1521/synthetic-service")]
    [InlineData(
        "//127.0.0.2:01521/synthetic-service:dedicated/synthetic-instance?retry_count=3",
        "//host.docker.internal:01521/synthetic-service:dedicated/synthetic-instance?retry_count=3"
    )]
    [InlineData(
        "tcps://[::1]:1521/synthetic-service?wallet_location=/synthetic-wallet&ssl_server_dn_match=yes",
        "tcps://host.docker.internal:1521/synthetic-service?wallet_location=/synthetic-wallet&ssl_server_dn_match=yes"
    )]
    [InlineData(
        "//[0:0:0:0:0:0:0:1]/synthetic-service",
        "//host.docker.internal/synthetic-service"
    )]
    [InlineData(
        "Data Source = ' //LOCALHOST:1521/synthetic-service '; User Id = synthetic-user; Password = \"synthetic;password\"; Pooling=false",
        "Data Source = ' //host.docker.internal:1521/synthetic-service '; User Id = synthetic-user; Password = \"synthetic;password\"; Pooling=false"
    )]
    public async Task EasyConnectChangesOnlyEligibleHostsInBothSources(
        string original,
        string expected
    )
    {
        using var cli = new UserSecretsCliHarness();
        cli.Configure("""{"userSecrets":["sample"]}""");
        Project(cli, new { Generated = original });
        cli.Store("sample", JsonSerializer.Serialize(new { Nested = new[] { original } }));
        MappedPorts(cli);
        var originals = Originals(cli);

        (await cli.Run("--verbose")).Should().Be(0, cli.Console.AllText);

        using var written = JsonDocument.Parse(Written(cli, "sample"));
        written.RootElement.GetProperty("Generated").GetString().Should().Be(expected);
        written.RootElement.GetProperty("Nested")[0].GetString().Should().Be(expected);
        SafeDiagnostics(cli);
        Unchanged(originals);
    }

    private static void Project(UserSecretsCliHarness cli, object settings)
    {
        cli.Root.File(
            "folder/Api/Api.csproj",
            "<Project><PropertyGroup><UserSecretsId>sample</UserSecretsId></PropertyGroup></Project>"
        );
        cli.Root.File("folder/Api/appsettings.json", JsonSerializer.Serialize(settings));
    }

    private static void MappedPorts(UserSecretsCliHarness cli) =>
        cli
            .Sbx.GetUserSecretsHostPortsAsync(cli.Name, Arg.Any<CancellationToken>())
            .Returns(new[] { 1521, 8004 });

    private static Dictionary<string, byte[]> Originals(UserSecretsCliHarness cli) =>
        Directory
            .EnumerateFiles(cli.Root.Path, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes);

    private static void Unchanged(Dictionary<string, byte[]> originals)
    {
        foreach (var (path, original) in originals)
            File.ReadAllBytes(path).Should().Equal(original);
    }

    private static void SafeDiagnostics(UserSecretsCliHarness cli) =>
        cli
            .Console.AllText.Should()
            .NotContain("synthetic-service")
            .And.NotContain("synthetic-user")
            .And.NotContain("synthetic;password")
            .And.NotContain("synthetic-instance")
            .And.NotContain("synthetic-wallet")
            .And.NotContain("remote.test");

    private static string Written(UserSecretsCliHarness cli, string id) =>
        (string)
            cli
                .Sbx.ReceivedCalls()
                .Last(call =>
                    call.GetMethodInfo().Name == "WriteUserSecretsAsync"
                    && (string)call.GetArguments()[1]! == id
                )
                .GetArguments()[2]!;
}
