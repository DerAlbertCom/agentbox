using Abox.Build;
using Abox.Configuration;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Launchers;
using Abox.Sbx;
using Abox.Services;

namespace Abox.Cli;

/// <summary>
/// The services the commands use. One place, so every command works the same way.
/// </summary>
public sealed class AboxServices
{
    public AboxServices(IConsole console)
    {
        Console = console;
        Paths = AboxPaths.Create();
        Config = AboxConfigLoader.Load(Paths);
        Runner = new ProcessRunner();
        HostPrograms = new HostPrograms();
        Sbx = new SbxClient(Runner);
        Preflight = new Preflight(console, Sbx, HostPrograms);
        Resolver = new SquadResolver(Config);
        Store = new BuildStore(Paths);
        Records = new SandboxRecordStore(Paths);
        PersonalConfigurations = new PersonalConfigurations(Paths);
        Launchers = new LauncherRegistry([new ShellLauncher(), new HerdrAppLauncher(Runner)]);
        Build = new BuildService(
            Resolver,
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            PersonalConfigurations,
            Store,
            Sbx,
            console
        );
        Create = new CreateService(Store, Records, Paths, Sbx, console);
        Run = new RunService(Launchers, Store, Records, Create, Sbx, HostPrograms, console);
    }

    public IConsole Console { get; }

    /// <summary>The arguments after the "--" of the command line.</summary>
    public TrailingArguments Trailing { get; } = new();

    public AboxPaths Paths { get; }

    public AboxConfig Config { get; }

    public IProcessRunner Runner { get; }

    public IHostPrograms HostPrograms { get; }

    public ISbxClient Sbx { get; }

    public Preflight Preflight { get; }

    public SquadResolver Resolver { get; }

    public BuildStore Store { get; }

    /// <summary>What abox knows about the sandboxes it created.</summary>
    public SandboxRecordStore Records { get; }

    public PersonalConfigurations PersonalConfigurations { get; }

    public LauncherRegistry Launchers { get; }

    public BuildService Build { get; }

    public CreateService Create { get; }

    public RunService Run { get; }
}
