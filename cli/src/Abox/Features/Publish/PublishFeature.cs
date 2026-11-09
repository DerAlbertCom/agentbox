using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Publish;

public static class PublishFeature
{
    public static IServiceCollection AddPublishFeature(this IServiceCollection services)
    {
        services.AddSingleton<PublishService>();
        services.AddSingleton<Command, PublishCommand>();
        return services;
    }
}
