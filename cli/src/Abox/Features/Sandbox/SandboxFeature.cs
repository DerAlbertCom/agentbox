using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Sandbox;

/// <summary>
/// The sandbox slice: the commands that reach an existing sandbox directly. They are
/// not user commands; other programs call them and they stay out of the help.
/// </summary>
public static class SandboxFeature
{
    public static IServiceCollection AddSandboxFeature(this IServiceCollection services)
    {
        services.AddSingleton<Command, ExecCommand>();
        services.AddSingleton<Command, PipeCommand>();
        return services;
    }
}
