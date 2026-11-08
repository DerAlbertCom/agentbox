using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Prune;

/// <summary>
/// The prune slice: <c>abox prune</c> and the service that does its work.
/// </summary>
public static class PruneFeature
{
    public static IServiceCollection AddPruneFeature(this IServiceCollection services)
    {
        services.AddSingleton<PruneService>();
        services.AddSingleton<Command, PruneCommand>();
        return services;
    }
}
