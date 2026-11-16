using System.Text.Json;
using Abox.Infrastructure;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

public sealed class PruneCommandTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task StopIsExplicitAndRuntimeInUseRefusalsRemainAuthoritative(bool full, bool stop)
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        var requests = UseRuntime(
            cli,
            JsonSerializer.Serialize(
                new[] { Template("sbx-kit-src", "old", "old", "2026-11-01T00:00:00Z") }
            ),
            refuseRemovals: true
        );
        var arguments = new List<string> { "prune" };
        if (full)
            arguments.Add("--full");
        if (stop)
            arguments.Add("--sbx");

        // Act
        var exitCode = await cli.Invoke(arguments.ToArray());

        // Assert
        exitCode.Should().Be(full ? 0 : 1);
        requests
            .Where(request => request.Arguments[0] == "stop")
            .Select(request => request.Arguments[1])
            .Should()
            .BeEquivalentTo<string>(stop ? ["unrelated-running-sandbox", "kit-builder"] : []);
        requests.Count(IsRemoval).Should().Be(1);
        if (stop)
            requests
                .FindLastIndex(request => request.Arguments[0] == "stop")
                .Should()
                .BeLessThan(requests.FindIndex(IsRemoval));
        requests
            .Should()
            .OnlyContain(request =>
                request.FileName == "sbx"
                && new[] { "version", "diagnose", "ls", "stop", "template" }.Contains(
                    request.Arguments[0]
                )
            );
        if (full)
            cli.Console.AllText.Should().Contain("sbx keeps");
    }

    [Fact]
    public async Task StopFailurePreventsTemplateInventoryAndRemoval()
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        var requests = UseRuntime(cli, "[]", failStop: true);

        // Act
        var exitCode = await cli.Invoke("prune", "--full", "--sbx");

        // Assert
        exitCode.Should().Be(1);
        cli.Console.AllText.Should().Contain("could not stop");
        requests.Should().NotContain(request => request.Arguments[0] == "template");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task OnlyProvenEligibleReferencesReachIndividualTemplateRemoval(
        bool full,
        bool uncertainSet
    )
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        var build = cli.Root.File("builds/team/squad/latest/build.json", "local build sentinel");
        var secret = cli.Store("synthetic-store", "synthetic secret sentinel");
        var inventory = new List<object>
        {
            Template("sandbox-kit-assembled", "new", "new", "2026-11-03T00:00:00Z"),
            Template("sandbox-kit-assembled", "old", "old", "2026-11-02T00:00:00Z"),
            Template("sbx-kit-src", "unused", "unused", "2026-11-01T00:00:00Z"),
            Template("sbx-kit-src", "unknown-age", "unknown-age", null),
            Template("sbx-kit-src", "unknown-id", null, "2026-11-01T00:00:00Z"),
            Template("other-sandbox-kit-assembled", "old", "other", "2026-11-01T00:00:00Z"),
            Template("other-sbx-kit-src", "unused", "other-source", "2026-11-01T00:00:00Z"),
            Template("agentbox-rwest-team/squad", "latest", "published", "2026-11-01T00:00:00Z"),
        };
        if (uncertainSet)
        {
            inventory.Add(Template("sandbox-kit-assembled", "no-age", "no-age", null));
            inventory.Add(Template("sandbox-kit-assembled", "no-id", null, "2026-11-01T00:00:00Z"));
            inventory.Add(
                Template("sandbox-kit-assembled", "no-id-template", "alias", "2026-11-01T00:00:00Z")
            );
        }
        var requests = UseRuntime(cli, JsonSerializer.Serialize(inventory));

        // Act
        var exitCode = await cli.Invoke(full ? ["prune", "--full"] : ["prune"]);

        // Assert
        exitCode.Should().Be(0);
        var expected = new List<string> { "docker.io/library/sandbox-kit-assembled:old" };
        if (full)
            expected.Add("docker.io/library/sandbox-kit-assembled:new");
        if (!uncertainSet)
            expected.Add("docker.io/library/sbx-kit-src:unused");
        requests
            .Where(IsRemoval)
            .Select(request => request.Arguments[2])
            .Should()
            .BeEquivalentTo(expected);
        requests
            .Should()
            .OnlyContain(request =>
                request.FileName == "sbx"
                && (
                    request.Arguments.SequenceEqual(new[] { "version", "--json" })
                    || request.Arguments.SequenceEqual(new[] { "diagnose", "--json" })
                    || request.Arguments.SequenceEqual(new[] { "template", "ls", "--json" })
                    || (
                        IsRemoval(request)
                        && request.Arguments.Count == 4
                        && request.Arguments[3] == "--force"
                    )
                )
            );
        File.ReadAllText(build).Should().Be("local build sentinel");
        File.ReadAllText(secret).Should().Be("synthetic secret sentinel");
    }

    [Fact]
    public async Task NewestTiesAndSharedSourcesAreRetainedAndReportedByBuildName()
    {
        // Arrange
        using var cli = new UserSecretsCliHarness();
        var requests = UseRuntime(cli, NewestTieInventory());

        // Act
        var exitCode = await cli.Invoke("prune");

        // Assert
        exitCode.Should().Be(0);
        requests
            .Where(IsRemoval)
            .Select(request => request.Arguments[2])
            .Should()
            .Equal(
                "docker.io/library/sandbox-kit-assembled:old-template",
                "docker.io/library/sbx-kit-src:unused"
            );
        cli.Console.AllText.Should()
            .Contain("Keeping assembled sets 'new-a new-b' and their kit sources.")
            .And.Contain("Removing 2 stale template images");
    }

    private static string NewestTieInventory() =>
        JsonSerializer.Serialize(
            new[]
            {
                Template("sandbox-kit-assembled", "new-b-kit-0", "shared", "2026-11-03T00:00:00Z"),
                Template("sandbox-kit-assembled", "old-template", "shared", "2026-11-01T00:00:00Z"),
                Template(
                    "sandbox-kit-assembled",
                    "new-a-template",
                    "alias",
                    "2026-11-03T00:00:00Z"
                ),
                Template("sbx-kit-src", "shared", "shared", "2026-11-01T00:00:00Z"),
                Template("sbx-kit-src", "unused", "unused", "2026-11-01T00:00:00Z"),
            }
        );

    private static object Template(string repository, string tag, string? id, string? created) =>
        new
        {
            repository = "docker.io/library/" + repository,
            tag,
            id,
            created_at = created,
        };

    private static bool IsRemoval(ProcessRequest request) =>
        request.Arguments.Count >= 3
        && request.Arguments[0] == "template"
        && request.Arguments[1] == "rm";

    private static List<ProcessRequest> UseRuntime(
        UserSecretsCliHarness cli,
        string inventory,
        bool refuseRemovals = false,
        bool failStop = false
    )
    {
        var requests = new List<ProcessRequest>();
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                var request = call.Arg<ProcessRequest>();
                requests.Add(request);
                return request.Arguments[0] switch
                {
                    "version" => new ProcessResult(0, """{"client":{"version":"0.51.0"}}""", ""),
                    "diagnose" => new ProcessResult(0, """{"checks":[]}""", ""),
                    "ls" => new ProcessResult(
                        0,
                        """[{"name":"unrelated-running-sandbox","status":"running"},{"name":"kit-builder","status":"running"},{"name":"stopped-sandbox","status":"stopped"}]""",
                        ""
                    ),
                    "stop" when failStop => new ProcessResult(1, "", "synthetic stop failure"),
                    "template" when request.Arguments[1] == "ls" => new ProcessResult(
                        0,
                        inventory,
                        ""
                    ),
                    "template" when refuseRemovals => new ProcessResult(
                        1,
                        "",
                        "image is used by a sandbox"
                    ),
                    _ => new ProcessResult(0, "", ""),
                };
            });
        cli.Services.AddSingleton(runner);
        cli.Services.AddSingleton<ISbxClient, SbxClient>();
        return requests;
    }
}
