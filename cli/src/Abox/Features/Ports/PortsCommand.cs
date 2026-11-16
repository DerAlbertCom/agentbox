using System.CommandLine;
using Abox.ApplicationPorts;
using Abox.Configuration;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Sandboxes;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.Ports;

public sealed class PortsCommand : Command
{
    public PortsCommand(
        Lazy<AboxConfig> config,
        Preflight preflight,
        SandboxResolver sandboxes,
        AppPorts ports,
        IConsole console
    )
        : base("ports", "Publish discovered application ports on host loopback with an offset.")
    {
        var location = new Option<string>("--location")
        {
            Description = "Sandbox folder. Defaults to defaultLocation, then the current folder.",
        };
        var offset = new Option<int?>("--offset")
        {
            Description =
                "One-run port offset (-65534..65534). Overrides abox.json portOffset, then default 10000.",
        };
        Options.Add(location);
        Options.Add(offset);
        SetAction(
            async (parseResult, cancellationToken) =>
                await CommandHandling.RunAsync(
                    async () =>
                    {
                        var folder = config.Value.ResolveLocation(parseResult.GetValue(location));
                        var folderConfig = await FolderConfig.LoadAsync(folder, cancellationToken);
                        var selectedOffset = FolderConfig.SelectPortOffset(
                            folderConfig,
                            parseResult.GetValue(offset)
                        );
                        if (!await preflight.EnsureReadyAsync(cancellationToken))
                            return 1;
                        var sandbox =
                            await sandboxes.FindAsync(folder, cancellationToken)
                            ?? throw new AboxException(
                                $"There is no sandbox for '{folder}'. Run 'abox create --location \"{folder}\" --team <name> --squad <name>' first; ports never creates or replaces one."
                            );
                        await ports.PublishAsync(
                            folder,
                            sandbox.Name,
                            selectedOffset,
                            cancellationToken
                        );
                        return 0;
                    },
                    console
                )
        );
    }
}

public static class PortsFeature
{
    public static IServiceCollection AddPortsFeature(this IServiceCollection services) =>
        services.AddSingleton<Command, PortsCommand>();
}
