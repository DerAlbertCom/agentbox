using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Create;

/// <summary>
/// The create slice: <c>abox create</c> and the service that does its work.
/// </summary>
public static class CreateFeature
{
    public static IServiceCollection AddCreateFeature(this IServiceCollection services)
    {
        services.AddSingleton<CreateService>();
        services.AddSingleton<Command, CreateCommand>();
        return services;
    }
}
