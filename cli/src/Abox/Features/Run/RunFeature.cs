using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Run;

/// <summary>
/// The run slice: <c>abox run</c> and the service that does its work.
/// </summary>
public static class RunFeature
{
    public static IServiceCollection AddRunFeature(this IServiceCollection services)
    {
        services.AddSingleton<RunService>();
        services.AddSingleton<Command, RunCommand>();
        return services;
    }
}
