using System.CommandLine;
using Abox.Configuration;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Launchers;
using Abox.Sbx;
using Abox.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Cli;

/// <summary>
/// The abox command line.
/// </summary>
public static class AboxCommand
{
    public static RootCommand Create(IServiceProvider services)
    {
        var root = new RootCommand("abox: AI agent sandboxes for your folders.")
        {
            Subcommands =
            {
                BuildCommand.Create(
                    services.GetRequiredService<AboxConfig>(),
                    services.GetRequiredService<Preflight>(),
                    services.GetRequiredService<BuildService>(),
                    services.GetRequiredService<IConsole>()
                ),
                CreateCommand.Create(
                    services.GetRequiredService<AboxConfig>(),
                    services.GetRequiredService<Preflight>(),
                    services.GetRequiredService<CreateService>(),
                    services.GetRequiredService<IConsole>()
                ),
                RunCommand.Create(
                    services.GetRequiredService<LauncherRegistry>(),
                    services.GetRequiredService<Preflight>(),
                    services.GetRequiredService<RunService>(),
                    services.GetRequiredService<TrailingArguments>(),
                    services.GetRequiredService<IConsole>()
                ),
                SecretsCommand.Create(
                    services.GetRequiredService<AboxConfig>(),
                    services.GetRequiredService<Preflight>(),
                    services.GetRequiredService<SecretsService>(),
                    services.GetRequiredService<IConsole>()
                ),
                PipeCommand.Create(
                    services.GetRequiredService<ISbxClient>(),
                    services.GetRequiredService<TrailingArguments>(),
                    services.GetRequiredService<IConsole>()
                ),
                ExecCommand.Create(
                    services.GetRequiredService<ISbxClient>(),
                    services.GetRequiredService<TrailingArguments>(),
                    services.GetRequiredService<IConsole>()
                ),
            },
        };

        return root;
    }
}
