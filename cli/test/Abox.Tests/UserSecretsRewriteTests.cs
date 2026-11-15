using System.Text.Json;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

public sealed class UserSecretsRewriteTests
{
    [Fact]
    public async Task LocalKitInventoryUsesSortedExactAuthorizedHostPortsWithoutHostKitSources()
    {
        using var fixture = new CopyFixture("abox-issue5-local-191454a2");
        fixture.Inventory = File.ReadAllText(RepositoryFile("local.policy.kit.json"));
        fixture.Store("sample", """{"Url":"https://127.0.0.1:8006/path?x=localhost:8004"}""");

        await fixture.CopyAsync();

        fixture
            .Value("sample", "Url")
            .Should()
            .Be("https://host.docker.internal:8006/path?x=localhost:8004");
        fixture.CheckedPorts.Should().Equal(1521, 4317, 4318, 8004, 8005, 8006, 8007, 13133, 16686);
        fixture
            .Requests[0]
            .Arguments.Should()
            .Equal(
                "policy",
                "ls",
                fixture.Sandbox,
                "--source",
                "kit",
                "--type",
                "network",
                "--json"
            );
    }

    [Theory]
    [InlineData(
        "Server=localhost,8004;Database=local;Password='localhost,8004;http://localhost:8004'",
        "Server=host.docker.internal,8004;Database=local;Password='localhost,8004;http://localhost:8004'"
    )]
    [InlineData(
        " Data Source = \"tcp:LOCALHOST,08004\" ;User Id=localhost;Password=synthetic",
        " Data Source = \"tcp:host.docker.internal,08004\" ;User Id=localhost;Password=synthetic"
    )]
    [InlineData(
        "Host=127.0.0.1; Port=8004 ;Username=localhost;Password=\"a;\"\"b\"",
        "Host=host.docker.internal; Port=8004 ;Username=localhost;Password=\"a;\"\"b\""
    )]
    [InlineData(
        "Server = 'localhost';Database=synthetic;Port = '8004'",
        "Server = 'host.docker.internal';Database=synthetic;Port = '8004'"
    )]
    public async Task OnlyRecognizedConnectionStringEndpointFieldsChange(
        string original,
        string expected
    )
    {
        using var fixture = new CopyFixture();
        fixture.UsePorts(8004);
        fixture.Store("sample", JsonSerializer.Serialize(new { Connection = original }));

        await fixture.CopyAsync();

        fixture.Value("sample", "Connection").Should().Be(expected);
    }

    [Theory]
    [InlineData("https://sql.internal:8004/path")]
    [InlineData("https://host.docker.internal:8004/path")]
    [InlineData("https://localhost:5432/path")]
    [InlineData("https://localhost/path")]
    [InlineData("https://[::1]:8004/path")]
    [InlineData("https://localhost.:8004/path")]
    [InlineData("https://127.1:8004/path")]
    [InlineData("https://localhost:8004/path with spaces")]
    [InlineData("https://localhost:8004/\\windows")]
    [InlineData("https://localhost:8004/path%ZZ")]
    [InlineData("https://localhost:8004/path%")]
    [InlineData("https://localhost:8004/path\u0000")]
    [InlineData("prefix http://localhost:8004")]
    [InlineData("Server=sql.internal,8004;Password=localhost,8004")]
    [InlineData("Server=localhost,5432")]
    [InlineData("Server=localhost\\instance,8004")]
    [InlineData("Server=localhost,8004;Server=other,8004")]
    [InlineData("Server=localhost,8004;Host=other;Port=8004")]
    [InlineData("Server=localhost;Port=8004;Port=5432")]
    [InlineData("Server=localhost,8004;Password='unterminated")]
    [InlineData("Server=localhost,8004;Password={a;b}")]
    [InlineData("Data Source=localhost:8004/database")]
    [InlineData("Password=http://localhost:8004;Path=localhost,8004")]
    [InlineData("localhost:8004")]
    public async Task NamedUndeclaredAndUnsupportedFormsRemainByteForByteUnchanged(string original)
    {
        using var fixture = new CopyFixture();
        fixture.UsePorts(8004);
        fixture.Store("sample", JsonSerializer.Serialize(new { Endpoint = original }));

        await fixture.CopyAsync();

        fixture.Value("sample", "Endpoint").Should().Be(original);
    }

    [Theory]
    [InlineData(
        "postgresql://user:p%40ss@LOCALHOST:08004/db?Password=localhost:8004#localhost:8004",
        "postgresql://user:p%40ss@host.docker.internal:08004/db?Password=localhost:8004#localhost:8004"
    )]
    [InlineData("tcp://localhost:8004", "tcp://host.docker.internal:8004")]
    [InlineData(
        "http://localhost:8004/path/%2f?q=%26",
        "http://host.docker.internal:8004/path/%2f?q=%26"
    )]
    public async Task UriRewritingChangesOnlyTheAuthorityHost(string original, string expected)
    {
        using var fixture = new CopyFixture();
        fixture.UsePorts(8004);
        fixture.Store("sample", JsonSerializer.Serialize(new { Endpoint = original }));

        await fixture.CopyAsync();

        fixture.Value("sample", "Endpoint").Should().Be(expected);
    }

    [Fact]
    public async Task PublishedEmptyInventoryIsValidAndDoesNotUseAmbientAuthorization()
    {
        using var fixture = new CopyFixture();
        fixture.Inventory = File.ReadAllText(RepositoryFile("published.policy.kit.json"));
        fixture.Store("sample", """{"Url":"http://localhost:8004"}""");

        await fixture.CopyAsync();

        fixture.Value("sample", "Url").Should().Be("http://localhost:8004");
        fixture.CheckedPorts.Should().BeEmpty();
    }

    [Fact]
    public async Task EffectiveDenyExcludesAnActiveKitAllowAndCandidatesAreDeduplicated()
    {
        using var fixture = new CopyFixture();
        fixture.UsePorts(8006, 8004, 8006);
        fixture.DeniedPorts.Add(8004);
        fixture.Store("first", """{"Url":"http://localhost:8004"}""");
        fixture.Store("last", """{"Url":"http://localhost:8006"}""");

        await fixture.CopyAsync();

        fixture.Value("first", "Url").Should().Be("http://localhost:8004");
        fixture.Value("last", "Url").Should().Be("http://host.docker.internal:8006");
        fixture.CheckedPorts.Should().Equal(8004, 8006);
        var firstWrite = fixture.Requests.FindIndex(request =>
            !string.IsNullOrEmpty(request.StandardInput)
        );
        fixture
            .Requests.Take(firstWrite)
            .Count(request => request.Arguments[0] == "policy")
            .Should()
            .Be(3);
        fixture
            .Requests.Take(firstWrite)
            .Should()
            .Contain(request =>
                request.Arguments.Contains("check") && request.Arguments[0] == "exec"
            );
    }

    [Fact]
    public async Task OriginalValuesAreDeniedBeforeAnyPolicyLookupOrRewriting()
    {
        using var fixture = new CopyFixture(
            config: new AboxConfig { UserSecretsDenyList = ["localhost"] }
        );
        fixture.UsePorts(8004);
        fixture.Store("first", """{"Password":"synthetic-only"}""");
        fixture.Store("last", """{"Connection":"Server=localhost,8004;Password=synthetic-only"}""");

        var copy = () => fixture.CopyAsync();
        var exception = (await copy.Should().ThrowAsync<AboxException>()).Which;

        exception
            .Message.Should()
            .Contain("last")
            .And.Contain("Connection")
            .And.NotContain("synthetic-only");
        fixture.Requests.Should().BeEmpty();
        var report = await fixture.CopyAsync(force: true);
        fixture
            .Value("last", "Connection")
            .Should()
            .Be("Server=host.docker.internal,8004;Password=synthetic-only");
        report.SafetyOverride.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaterPolicyOrDestinationFailurePreventsAllWritesEvenWithForce(
        bool destination
    )
    {
        using var fixture = new CopyFixture();
        fixture.UsePorts(8004, 8006);
        fixture.Store("first", """{"Url":"http://localhost:8004"}""");
        fixture.Store("last", """{"Url":"http://localhost:8006"}""");
        fixture.ResultOverride = request =>
            (destination && request.Arguments[0] == "exec")
            || (!destination && request.Arguments[^1] == "localhost:8006")
                ? new ProcessResult(9, "synthetic-password", "123456789 true")
                : null;

        var copy = () => fixture.CopyAsync(force: true);
        var exception = (await copy.Should().ThrowAsync<AboxException>()).Which;

        fixture
            .Requests.Should()
            .NotContain(request => !string.IsNullOrEmpty(request.StandardInput));
        exception.ToString().Should().NotContain("synthetic-password").And.NotContain("123456789");
        if (!destination)
            exception.Message.Should().Contain("policy ls").And.Contain("policy check");
    }

    [Fact]
    public async Task APreparedSnapshotIsReusableWithoutMutatingItsOriginalHostValues()
    {
        using var fixture = new CopyFixture();
        fixture.UsePorts(8004);
        fixture.Store("sample", """{"Url":"http://localhost:8004"}""");
        var prepared = await fixture.PrepareAsync();

        await fixture.CopyPreparedAsync(prepared);
        fixture.Value("sample", "Url").Should().Be("http://host.docker.internal:8004");
        fixture.DeniedPorts.Add(8004);
        await fixture.CopyPreparedAsync(prepared);

        fixture.Value("sample", "Url").Should().Be("http://localhost:8004");
        File.ReadAllText(fixture.SourcePath("sample"))
            .Should()
            .Be("""{"Url":"http://localhost:8004"}""");
    }

    [Fact]
    public async Task NestedStringsAreRewrittenWithoutCopyingSkippedCommentPayloads()
    {
        using var fixture = new CopyFixture();
        fixture.UsePorts(8004);
        fixture.Store(
            "sample",
            """
            { /* synthetic-hidden-comment */ "Nested":{"Items":[
              {"Url":"http://localhost:8004","Number":8004,"Flag":true,"Null":null},
              "Server=localhost,8004"]}, "Empty":{} }
            """
        );

        await fixture.CopyAsync();

        using var written = JsonDocument.Parse(fixture.Written("sample"));
        var items = written.RootElement.GetProperty("Nested").GetProperty("Items");
        items[0].GetProperty("Url").GetString().Should().Be("http://host.docker.internal:8004");
        items[0].GetProperty("Number").GetInt32().Should().Be(8004);
        items[0].GetProperty("Flag").GetBoolean().Should().BeTrue();
        items[0].GetProperty("Null").ValueKind.Should().Be(JsonValueKind.Null);
        items[1].GetString().Should().Be("Server=host.docker.internal,8004");
        fixture.Written("sample").Should().NotContain("synthetic-hidden-comment");
    }

    [Fact]
    public async Task MixedFullNestedAndFilteredFlatSnapshotsUseOnlySelectedOriginalValues()
    {
        using var fixture = new CopyFixture(
            config: new AboxConfig { UserSecretsDenyList = ["synthetic-production"] }
        );
        fixture.UsePorts(8004);
        fixture.Store("full", """{"Nested":{"Url":"http://localhost:8004"},"Flag":true}""");
        fixture.Store(
            "filtered",
            """
            {"Nodes":[{"Url":"http://localhost:5432"},{"Url":"http://localhost:8004"}],
             "Password":"synthetic-production", "Named":"http://named.internal:8004"}
            """
        );
        fixture.KeyPatterns["filtered"] = ["Nodes:1:*", "Named"];
        fixture.Store("empty", """{"Password":"synthetic-production"}""");
        fixture.KeyPatterns["empty"] = [];

        var report = await fixture.CopyAsync();

        using var full = JsonDocument.Parse(fixture.Written("full"));
        full.RootElement.GetProperty("Nested")
            .GetProperty("Url")
            .GetString()
            .Should()
            .Be("http://host.docker.internal:8004");
        fixture.Value("filtered", "Nodes:1:Url").Should().Be("http://host.docker.internal:8004");
        fixture.Value("filtered", "Named").Should().Be("http://named.internal:8004");
        fixture
            .Written("filtered")
            .Should()
            .NotContain("synthetic-production")
            .And.NotContain("5432");
        fixture.Written("empty").Should().Be("{}");
        report.Stores.Select(store => store.KeyCount).Should().Equal(2, 2, 0);
    }

    [Fact]
    public async Task ReportsContainOnlyChangedEffectiveKeysAndKeepOriginalForceAccounting()
    {
        using var fixture = new CopyFixture(
            config: new AboxConfig { UserSecretsDenyList = ["localhost"] }
        );
        fixture.UsePorts(8004);
        fixture.Store(
            "full",
            """
            {"Nested":{"Url":"http://localhost:8004/secret-path"},
             "Nodes":["Server=127.0.0.1,8004;Password=synthetic-password"],
             "Unmapped":"http://localhost:5432"}
            """
        );
        fixture.Store(
            "flat",
            """{"Nested":{"Url":"http://localhost:8004"},"Named":"http://named.internal:8004"}"""
        );
        fixture.KeyPatterns["flat"] = ["Nested:*"];
        fixture.Store("unchanged", """{"Url":"https://named.internal:8004"}""");

        var report = await fixture.CopyAsync(force: true);

        report
            .SafetyOverride.Should()
            .BeTrue(
                "the original selected localhost values matched, even after rewriting hides them"
            );
        report.Stores[0].RewrittenKeys.Should().Equal("Nested:Url", "Nodes:0");
        report.Stores[0].RewrittenKeyCount.Should().Be(2);
        report.Stores[0].KeyCount.Should().Be(3);
        report.Stores[1].RewrittenKeys.Should().Equal("Nested:Url");
        report.Stores[2].RewrittenKeys.Should().BeEmpty();
        report.ToString().Should().NotContain("secret-path").And.NotContain("synthetic-password");
        string.Join(" ", report.Stores)
            .Should()
            .NotContain("secret-path")
            .And.NotContain("synthetic-password");
    }

    private static string RepositoryFile(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "docs")))
            directory = directory.Parent;
        return Path.Combine(directory!.FullName, "docs/verification/user-secrets/metadata", name);
    }

    private sealed class CopyFixture : IDisposable
    {
        private readonly TempDirectory root = new();
        private readonly List<string> ids = [];
        private readonly Dictionary<string, string> written = [];
        private readonly UserSecretsCopy copy;

        internal CopyFixture(string sandbox = "synthetic-sandbox", AboxConfig? config = null)
        {
            Sandbox = sandbox;
            var runner = Substitute.For<IProcessRunner>();
            runner
                .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    var request = call.Arg<ProcessRequest>();
                    Requests.Add(request);
                    if (ResultOverride?.Invoke(request) is { } overridden)
                        return overridden;
                    var args = request.Arguments;
                    if (args[0] == "policy" && args[1] == "ls")
                        return new ProcessResult(0, Inventory, "");
                    if (args[0] == "policy" && args[1] == "check")
                    {
                        var target = args[^1];
                        var port = int.Parse(target.Split(':')[1]);
                        CheckedPorts.Add(port);
                        return new ProcessResult(
                            0,
                            JsonSerializer.Serialize(
                                new
                                {
                                    action = "net:connect:tcp",
                                    allowed = !DeniedPorts.Contains(port),
                                    context = "sandbox:" + Sandbox,
                                    resource_type = "net:domain",
                                    resource_value = target,
                                    target,
                                    type = "network",
                                }
                            ),
                            ""
                        );
                    }
                    if (args[0] == "exec" && request.StandardInput is { Length: > 0 } frame)
                    {
                        var payload = frame.IndexOf('\n', frame.IndexOf('\n') + 1) + 1;
                        written[args[^1]] = frame[payload..];
                    }
                    return new ProcessResult(0, "", "");
                });
            copy = new UserSecretsCopy(
                config ?? new AboxConfig(),
                new UserSecretsHost(null, root.Path),
                new SbxClient(runner, AboxPaths.For(root.Path, root.Path))
            );
        }

        internal string Sandbox { get; }
        internal string Inventory { get; set; } = """{"rules":[]}""";
        internal List<ProcessRequest> Requests { get; } = [];
        internal List<int> CheckedPorts { get; } = [];
        internal HashSet<int> DeniedPorts { get; } = [];
        internal Func<ProcessRequest, ProcessResult?>? ResultOverride { get; set; }
        internal Dictionary<string, string[]> KeyPatterns { get; } = [];

        internal string Written(string id) => written[id];

        internal string SourcePath(string id) =>
            Path.Combine(root.Path, $".microsoft/usersecrets/{id}/secrets.json");

        internal void UsePorts(params int[] ports) =>
            Inventory = JsonSerializer.Serialize(
                new
                {
                    rules = new[]
                    {
                        new
                        {
                            name = "kit:" + Sandbox,
                            scope = "sandbox:" + Sandbox,
                            applies_to = "sandbox:" + Sandbox,
                            resource_type = "network",
                            decision = "allow",
                            origin = "scoped",
                            layer = "local",
                            status = "active",
                            editable = false,
                            sandbox_id = Sandbox,
                            provenance = new { created_via = "provisioned", source = "sandboxes" },
                            actions = new[] { "net:connect:tcp" },
                            resources = ports.Select(port => "localhost:" + port),
                        },
                    },
                }
            );

        internal void Store(string id, string json)
        {
            ids.Add(id);
            root.File($".microsoft/usersecrets/{id}/secrets.json", json);
        }

        internal async Task<PreparedUserSecrets> PrepareAsync(bool force = false)
        {
            root.File(
                "abox.json",
                JsonSerializer.Serialize(
                    new
                    {
                        userSecrets = ids.Select(id =>
                                KeyPatterns.TryGetValue(id, out var keys)
                                    ? (object)new { id, keys }
                                    : id
                            )
                            .ToArray(),
                    }
                )
            );
            return await copy.PrepareAsync(
                await FolderConfig.LoadAsync(root.Path, CancellationToken.None),
                force,
                CancellationToken.None
            );
        }

        internal async Task<UserSecretsReport> CopyAsync(bool force = false) =>
            await CopyPreparedAsync(await PrepareAsync(force));

        internal Task<UserSecretsReport> CopyPreparedAsync(PreparedUserSecrets prepared) =>
            copy.CopyAsync(prepared, Sandbox, CancellationToken.None);

        internal string? Value(string id, string key)
        {
            using var json = JsonDocument.Parse(written[id]);
            return json.RootElement.GetProperty(key).GetString();
        }

        public void Dispose() => root.Dispose();
    }
}
