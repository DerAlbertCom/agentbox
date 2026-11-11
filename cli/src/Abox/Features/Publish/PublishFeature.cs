using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Publish;

/// <summary>
/// The publish slice: <c>abox publish</c> and the service that does its work.
/// </summary>
public static class PublishFeature
{
    public static IServiceCollection AddPublishFeature(this IServiceCollection services)
    {
        services.AddSingleton<PublishService>();
        services.AddSingleton<Command, PublishCommand>();
        return services;
    }
}
