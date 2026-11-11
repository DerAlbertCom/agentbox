using System.CommandLine;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Create;

/// <summary>
/// The create slice: <c>abox create</c> and the service that does its work.
/// </summary>
public static class CreateFeature
{
    public static IServiceCollection AddCreateFeature(this IServiceCollection services)
    {
        services.AddSingleton(provider => new Lazy<SandboxCreator>(
            provider.GetRequiredService<SandboxCreator>
        ));
        services.AddSingleton<Command, CreateCommand>();
        return services;
    }
}
