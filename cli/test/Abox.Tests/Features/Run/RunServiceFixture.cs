using Abox.Configuration;
using Abox.Features.Run;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Launchers;
using Abox.LocalState;
using Abox.Sandboxes;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

internal sealed class RunServiceFixture
{
    internal TempDirectory Root { get; } = new();
    internal TestConsole Console { get; } = new();
    internal ISbxClient Sbx { get; } = Substitute.For<ISbxClient>();
    internal RunService Service { get; }
    internal AboxPaths Paths { get; }
    internal BuildStore Store { get; }
    internal SandboxRecordStore Records { get; }
    internal SandboxCreator Create { get; }
    internal AboxConfig Config { get; } = new() { DefaultTeam = "sample" };

    internal RunServiceFixture()
    {
        var launchers = new LauncherRegistry([
            new ShellLauncher(Sbx),
            new HerdrAppLauncher(Substitute.For<IProcessRunner>(), Sbx, Console),
        ]);
        var hostPrograms = Substitute.For<IHostPrograms>();
        hostPrograms.Exists(Arg.Any<string>()).Returns(true);
        Paths = AboxPaths.For(Path.Combine(Root.Path, "config"), Path.Combine(Root.Path, "state"));
        Store = new BuildStore(Paths);
        Records = new SandboxRecordStore(Paths);
        Create = TestServiceGraphs.Creator(
            Records,
            Paths,
            Sbx,
            Console,
            new LocalSquadSource(Store, LocalWorkloadFactory.Images(), Console),
            new RemoteSquadSource(Config, Sbx, PublishedSquadFactory.Source()),
            new UserSecretsCopy(Config, new UserSecretsHost(null, null), Sbx)
        );
        Service = new RunService(
            launchers,
            new SquadResolver(Config),
            Store,
            Records,
            Create,
            Sbx,
            SandboxFactory.Resolver(Sbx, SandboxFactory.Hints(Config, Store, Records)),
            SandboxFactory.Hints(Config, Store, Records),
            hostPrograms,
            Console
        );
        Sbx.ExecAsync(Arg.Any<SbxExecRequest>(), Arg.Any<CancellationToken>()).Returns(0);
        Sbx.HasProgramAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
    }
}
