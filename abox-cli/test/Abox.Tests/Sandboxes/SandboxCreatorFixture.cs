using Abox.Configuration;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sandboxes;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

internal sealed class SandboxCreatorFixture
{
    internal TempDirectory Root { get; } = new();
    internal TestConsole Console { get; } = new();
    internal ISbxClient Sbx { get; } = Substitute.For<ISbxClient>();
    internal IPublishedSquadSource Published { get; } = PublishedSquadFactory.Source();
    internal AboxPaths Paths { get; }
    internal BuildStore Store { get; }
    internal SandboxRecordStore Records { get; }
    internal SandboxCreator Service { get; }

    internal SandboxCreatorFixture()
    {
        Paths = AboxPaths.For(Path.Combine(Root.Path, "config"), Path.Combine(Root.Path, "state"));
        Store = new BuildStore(Paths);
        Records = new SandboxRecordStore(Paths);
        Service = Creator();
        Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
        Sbx.RemoveSandboxAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
        Sbx.ListMcpServersAsync(Arg.Any<CancellationToken>()).Returns((IReadOnlyList<string>)[]);
        Sbx.AddMcpServerAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
    }

    internal SandboxCreator Creator(
        RemoteSquadSource? remote = null,
        LocalWorkloadImages? images = null
    ) =>
        TestServiceGraphs.Creator(
            Records,
            Paths,
            Sbx,
            Console,
            new LocalSquadSource(Store, images ?? LocalWorkloadFactory.Images(), Console),
            remote ?? new RemoteSquadSource(new AboxConfig(), Sbx, Published),
            new UserSecretsCopy(new AboxConfig(), new UserSecretsHost(null, null), Sbx)
        );

    internal SandboxCreationRequest Request() =>
        new(
            new SquadResolver.ResolvedTeam("sample", "teams/sample", ["ac"]),
            "ac",
            Root.Path,
            "latest",
            FolderConfig: null,
            Local: true
        );

    internal void SaveBuild(List<ManifestMcp>? mcp = null, string? reference = null)
    {
        Store.Save(
            new BuildManifest
            {
                Squad = "ac",
                Tag = "latest",
                Reference = reference ?? ImageName.OfTeam("sample", "latest"),
                WorkloadKitDirectory = "/kits/workload",
                LocalWorkload = LocalWorkloadFactory.Artifact(Root),
                StateMixinDirectory = "/kits/state",
                Kits =
                [
                    new ManifestKit
                    {
                        Name = "pi-config",
                        Reference = "/kits/pi-config",
                        IsLocal = true,
                        Arguments = new Dictionary<string, string> { ["mode"] = "team" },
                    },
                ],
                State =
                [
                    new ManifestState
                    {
                        SandboxPath = "/home/agent/.config/opencode/state",
                        RelativePath = ".config/opencode/state",
                    },
                ],
                Mcp = mcp ?? [],
            }
        );
    }
}
