using System.Text.Json;
using System.Text.Json.Nodes;
using Abox.Configuration;
using Abox.Features.Build;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sandboxes;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

public sealed partial class PublishedSquadSourceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BuildAndCreatePreserveRegisteredNamesAndExpandedMcpUrls(bool local)
    {
        // Arrange
        using var root = new TempDirectory();
        var paths = AboxPaths.For(root.Directory_("config"), Path.Combine(root.Path, "state"));
        root.File(
            "teams/mti-ca/base.abox",
            """
            workload: sample
            mcp:
              - azuredevops
            parameters:
              endpoint: https://example.com
            """
        );
        root.File(
            "teams/mti-ca/ac.abox",
            """
            base: base
            mcp:
              - name: elastic
                url: ${{ parameters.endpoint }}/mcp
            """
        );
        root.File("teams/mti-ca/sample.workload", "template: example.com/base:1\n");
        root.File(
            "config/personal.abox",
            """
            mcp:
              - name: database
            """
        );
        var config = new AboxConfig();
        var resolver = new SquadResolver(config);
        var team = resolver.ResolveTeam(root.Path, "mti-ca");
        var store = new BuildStore(paths);
        var images = LocalWorkloadTest.Images();
        var sbx = Substitute.For<ISbxClient>();
        sbx.ListMcpServersAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<string>)["azuredevops", "database"]);
        sbx.AddMcpServerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        sbx.RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, ""));
        var build = new BuildService(
            resolver,
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            new PersonalConfigurations(paths),
            store,
            images,
            sbx,
            _console
        );
        await build.BuildAsync(
            new BuildRequest([team], root.Path, "latest", Verify: false, MetadataOnly: !local),
            CancellationToken.None
        );
        var manifest = store.Load("ac", "latest")!;
        _metadata["Mcp"] = JsonSerializer.SerializeToNode(manifest.Mcp);
        _metadata["State"] = new JsonArray();
        var name = SandboxName.For(root.Path);
        sbx.FindSandboxAsync(name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(name, "running", DateTimeOffset.UtcNow));
        var records = new SandboxRecordStore(paths);
        var creator = new SandboxCreator(
            records,
            paths,
            SandboxTest.Resolver(sbx),
            sbx,
            _console,
            new LocalSquadSource(store, images, _console),
            new RemoteSquadSource(
                config,
                sbx,
                new PublishedSquadSource(new OciRegistry(_runner), _programs, _console)
            ),
            new UserSecretsCopy(config, new UserSecretsHost(null, null), sbx)
        );

        // Act
        var exitCode = await creator.CreateAsync(
            new SandboxCreationRequest(
                team,
                "ac",
                root.Path,
                "latest",
                FolderConfig: null,
                AssumeYes: true,
                Local: local
            ),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        manifest
            .Mcp.Should()
            .BeEquivalentTo(
                new ManifestMcp[]
                {
                    new() { Name = "azuredevops" },
                    new() { Name = "elastic", Url = "https://example.com/mcp" },
                    new() { Name = "database" },
                },
                options => options.WithStrictOrdering()
            );
        await sbx.Received(1)
            .AddMcpServerAsync("elastic", "https://example.com/mcp", Arg.Any<CancellationToken>());
        sbx.ReceivedCalls()
            .Count(call => call.GetMethodInfo().Name == nameof(ISbxClient.AddMcpServerAsync))
            .Should()
            .Be(1, "name-only declarations must use existing registrations");
        await sbx.Received(1).RemoveSandboxAsync(name, Arg.Any<CancellationToken>());
        await sbx.Received(1)
            .CreateAsync(
                Arg.Is<SbxCreateRequest>(request =>
                    request.StaticMcpServers.SequenceEqual(
                        new[] { "azuredevops", "elastic", "database" }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
        records.Load(name)!.Team.Should().Be("mti-ca");
        _calls
            .Should()
            .OnlyContain(call =>
                !call.Arguments.Contains("build") && !call.Arguments.Contains("create")
            );
        if (!local)
            _calls
                .Should()
                .Contain(
                    call => call.Arguments[0] == "image",
                    "remote create must read the published metadata"
                );
    }
}
