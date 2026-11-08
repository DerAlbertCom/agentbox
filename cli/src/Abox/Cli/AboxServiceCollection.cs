using Abox.Build;
using Abox.Configuration;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Launchers;
using Abox.Sbx;
using Abox.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Cli;

/// <summary>
/// The composition root: the services the commands use, registered in one place, so
/// every command works the same way.
/// </summary>
public static class AboxServiceCollection
{
    public static IServiceCollection AddAbox(
        this IServiceCollection services,
        IConsole console,
        TrailingArguments trailing
    )
    {
        services.AddSingleton(console);
        services.AddSingleton(trailing);
        services.AddSingleton(AboxPaths.Create());
        services.AddSingleton(provider =>
            AboxConfigLoader.Load(provider.GetRequiredService<AboxPaths>())
        );
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IHostPrograms, HostPrograms>();
        services.AddSingleton<ISbxClient, SbxClient>();
        services.AddSingleton<Preflight>();
        services.AddSingleton<SquadResolver>();
        services.AddSingleton<BuildStore>();
        services.AddSingleton<SandboxRecordStore>();
        services.AddSingleton<PersonalConfigurations>();
        services.AddSingleton<WorkloadKitGenerator>();
        services.AddSingleton<StateMixinGenerator>();
        services.AddSingleton<ILauncher, ShellLauncher>();
        services.AddSingleton<ILauncher, HerdrAppLauncher>();
        services.AddSingleton(provider => new LauncherRegistry(provider.GetServices<ILauncher>()));
        services.AddSingleton<BuildService>();
        services.AddSingleton<CreateService>();
        services.AddSingleton<SecretsService>();
        services.AddSingleton<RunService>();
        return services;
    }
}
