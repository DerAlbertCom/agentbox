using System.Text.Json;
using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.ApplicationPorts;

/// <summary>Shared discovery, reporting and runtime publishing for an already selected sandbox.</summary>
public sealed class AppPorts(ISbxClient sbx, IConsole console)
{
    public async Task PublishAsync(
        string location,
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        var occurrences = await DotnetLaunchSettings.DiscoverAsync(location, cancellationToken);
        foreach (var occurrence in occurrences)
            if (occurrence.Port + 10000 > 65535)
                throw new AboxException(
                    $"Invalid host port for {JsonSerializer.Serialize(occurrence.SourceFile)}: sandbox port {occurrence.Port} plus offset 10000 exceeds 65535. Fix applicationUrl before publishing; no ports were published."
                );
        if (occurrences.Count == 0)
        {
            console.WriteLine("No explicit application ports found; nothing published.");
            return;
        }

        console.WriteLine("Source file (profile) | Sandbox port | Host port");
        foreach (var occurrence in occurrences)
            console.WriteLine(
                $"{JsonSerializer.Serialize(occurrence.SourceFile)} ({JsonSerializer.Serialize(occurrence.Profile)}) | {occurrence.Port} | {occurrence.Port + 10000}"
            );
        foreach (
            var duplicate in occurrences
                .GroupBy(occurrence => occurrence.Port)
                .Where(group => group.Count() > 1)
        )
            console.WriteLine(
                $"Warning: sandbox port {duplicate.Key} has {duplicate.Count()} occurrences; they share host port {duplicate.Key + 10000}, published once."
            );
        try
        {
            await sbx.EnsureApplicationPortsAsync(
                sandbox,
                occurrences
                    .Select(occurrence => new ApplicationPortMapping(
                        occurrence.Port,
                        occurrence.Port + 10000
                    ))
                    .Distinct()
                    .ToArray(),
                cancellationToken
            );
        }
        catch (Exception exception) when (exception is AboxException or OperationCanceledException)
        {
            console.WriteError(
                $"Application-port publishing did not complete. Some mappings may remain published; no rollback is attempted. Inspect sandbox ports, free conflicting host ports if needed, then retry 'abox ports --location {JsonSerializer.Serialize(location)}'."
            );
            throw;
        }
        console.WriteLine(
            "Application ports published on 127.0.0.1 (existing matching mappings reused). HTTPS certificates are not configured by this command."
        );
    }
}
