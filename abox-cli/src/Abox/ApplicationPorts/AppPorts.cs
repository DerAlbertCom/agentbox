using System.Text.Json;
using Abox.Configuration;
using Abox.Https;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;

namespace Abox.ApplicationPorts;

internal sealed record PreparedAppPorts(
    string Location,
    int Offset,
    AppPortDiscoveryPlan Discovery
);

/// <summary>Shared discovery, reporting and runtime publishing for an already selected sandbox.</summary>
public sealed class AppPorts(ISbxClient sbx, IConsole console, SandboxHttps https)
{
    public async Task PublishAsync(
        string location,
        string sandbox,
        int offset,
        CancellationToken cancellationToken
    )
    {
        var prepared = await PrepareAsync(location, offset, cancellationToken);
        await PublishAsync(prepared, sandbox, cancellationToken);
    }

    internal static async Task<PreparedAppPorts> PrepareAsync(
        string location,
        int offset,
        CancellationToken cancellationToken
    )
    {
        FolderConfig.SelectPortOffset(null, offset);
        var plan = await AppPortDiscovery.DiscoverAsync(location, cancellationToken);
        var occurrences = plan.Occurrences;
        foreach (var occurrence in occurrences)
            if (!TcpPort.IsValid((long)occurrence.Port + offset))
                throw new AboxException(
                    $"Invalid host port for {JsonSerializer.Serialize(occurrence.SourceFile)}: sandbox port {occurrence.Port} plus offset {offset} must be in 1..65535. Fix the port or offset before publishing; no ports were published."
                );
        return new PreparedAppPorts(location, offset, plan);
    }

    internal async Task PublishAsync(
        PreparedAppPorts prepared,
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (location, offset, plan) = prepared;
        var occurrences = plan.Occurrences;
        if (occurrences.Count == 0)
        {
            console.WriteLine("No explicit application ports found; nothing published.");
            return;
        }

        console.WriteLine("Source file (profile/configuration) | Sandbox port | Host port");
        foreach (var occurrence in occurrences)
            console.WriteLine(
                $"{JsonSerializer.Serialize(occurrence.SourceFile)} ({JsonSerializer.Serialize(occurrence.Profile)}) | {occurrence.Port} | {occurrence.Port + offset}"
            );
        foreach (
            var duplicate in occurrences
                .GroupBy(occurrence => occurrence.Port)
                .Where(group => group.Count() > 1)
        )
            console.WriteLine(
                $"Warning: sandbox port {duplicate.Key} has {duplicate.Count()} occurrences; they share host port {duplicate.Key + offset}, published once."
            );
        try
        {
            await sbx.EnsureApplicationPortsAsync(
                sandbox,
                occurrences
                    .Select(occurrence => new ApplicationPortMapping(
                        occurrence.Port,
                        occurrence.Port + offset
                    ))
                    .Distinct()
                    .ToArray(),
                cancellationToken
            );
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception) when (exception is AboxException or OperationCanceledException)
        {
            console.WriteError(
                $"Application-port publishing did not complete. Some mappings may remain published and sandbox relays may remain running; no rollback is attempted. Inspect sandbox ports and relay diagnostics, free conflicting ports if needed, then retry 'abox ports --location {JsonSerializer.Serialize(location)} --offset {offset}'."
            );
            throw;
        }
        console.WriteLine(
            "Application ports published on 127.0.0.1 via sandbox relays (existing matching mappings reused)."
        );
        if (plan.HasDotnetProjects)
            await https.ConfigureAsync(sandbox, cancellationToken);
        else
            console.WriteLine(
                "No explicit ASP.NET Core Web SDK project found; HTTPS certificates are not configured for this folder."
            );
    }
}
