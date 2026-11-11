using Abox.Build;
using Abox.Configuration;
using Abox.Features.Build;
using Abox.Features.Create;
using Abox.Features.Prune;
using Abox.Features.Publish;
using Abox.Features.Run;
using Abox.Features.Sandbox;
using Abox.Features.Secrets;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Launchers;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Cli;

/// <summary>
/// The composition root: the shared services and, one call each, the feature slices.
/// Every slice registers its own command, so the commands are built by the container,
/// never wired by hand.
/// </summary>
public static class AboxServiceCollection
{
    public static IServiceCollection AddAbox(
        this IServiceCollection services,
        IConsole console,
        TrailingArguments trailing
    )
    {
        services
            .AddSharedServices(console, trailing)
            .AddBuildFeature()
            .AddPublishFeature()
            .AddCreateFeature()
            .AddRunFeature()
            .AddSecretsFeature()
            .AddPruneFeature()
            .AddSandboxFeature();

        // The root holds all the slice commands, in registration order.
        services.AddSingleton<AboxRootCommand>();

        return services;
    }

    private static IServiceCollection AddSharedServices(
        this IServiceCollection services,
        IConsole console,
        TrailingArguments trailing
    )
    {
        services.AddSingleton(console);
        services.AddSingleton(trailing);
        services.AddSingleton(AboxPaths.Create());
        services.AddSingleton(provider => new Lazy<AboxConfig>(() =>
            AboxConfigLoader.Load(provider.GetRequiredService<AboxPaths>())
        ));
        services.AddSingleton(provider => provider.GetRequiredService<Lazy<AboxConfig>>().Value);
        // Commands describe their syntax eagerly, but resolve configuration-dependent work only in actions.
        services.AddSingleton(provider => new Lazy<SquadResolver>(
            provider.GetRequiredService<SquadResolver>
        ));
        services.AddSingleton(provider => new Lazy<BuildService>(
            provider.GetRequiredService<BuildService>
        ));
        services.AddSingleton(provider => new Lazy<CreateService>(
            provider.GetRequiredService<CreateService>
        ));
        services.AddSingleton(provider => new Lazy<RunService>(
            provider.GetRequiredService<RunService>
        ));
        services.AddSingleton(provider => new Lazy<SecretsService>(
            provider.GetRequiredService<SecretsService>
        ));
        services.AddSingleton<ProcessOutput>();
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
        services.AddSingleton<LauncherRegistry>();
        return services;
    }
}
