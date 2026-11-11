using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Secrets;

/// <summary>
/// The secrets slice: <c>abox secrets</c> and the service that does its work.
/// </summary>
public static class SecretsFeature
{
    public static IServiceCollection AddSecretsFeature(this IServiceCollection services)
    {
        services.AddSingleton<SecretsService>();
        services.AddSingleton(provider => new Lazy<SecretsService>(
            provider.GetRequiredService<SecretsService>
        ));
        services.AddSingleton<Command, SecretsCommand>();
        return services;
    }
}
