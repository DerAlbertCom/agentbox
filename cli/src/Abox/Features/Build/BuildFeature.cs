using System.CommandLine;
using Abox.Kits;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Build;

/// <summary>
/// The build slice: <c>abox build</c> and the service that does its work.
/// </summary>
public static class BuildFeature
{
    public static IServiceCollection AddBuildFeature(this IServiceCollection services)
    {
        services.AddSingleton<BuildService>();
        services.AddSingleton<WorkloadKitGenerator>();
        services.AddSingleton<StateMixinGenerator>();
        services.AddSingleton<Command, BuildCommand>();
        return services;
    }
}
