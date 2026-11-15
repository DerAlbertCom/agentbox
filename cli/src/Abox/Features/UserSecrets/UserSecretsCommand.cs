using System.CommandLine;
using Abox.Configuration;
using Abox.Health;
using Abox.Infrastructure;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sbx;
using Abox.UserSecrets;
using Microsoft.Extensions.DependencyInjection;

namespace Abox.Features.UserSecrets;

public sealed class UserSecretsCommand : Command
{
    public UserSecretsCommand(
        Lazy<AboxConfig> config,
        Preflight preflight,
        Lazy<UserSecretsCopy> copy,
        Lazy<SandboxRecordStore> records,
        ISbxClient sbx,
        IConsole console
    )
        : base(
            "user-secrets",
            "Copy explicitly selected .NET user-secret stores into this folder's existing sandbox."
        )
    {
        var location = new Option<string>("--location")
        {
            Description = "Sandbox folder. Defaults to defaultLocation, then the current folder.",
        };
        var force = new Option<bool>("--force")
        {
            Description = "Override only the global user-secret value deny list.",
        };
        Options.Add(location);
        Options.Add(force);
        SetAction(
            async (parseResult, cancellationToken) =>
                await CommandHandling.RunAsync(
                    async () =>
                    {
                        var folder = config.Value.ResolveLocation(parseResult.GetValue(location));
                        var folderConfig =
                            await FolderConfig.LoadAsync(folder, cancellationToken)
                            ?? throw new AboxException(
                                "No abox.json in this folder. Declare 'userSecrets' explicitly before copying."
                            );
                        if (folderConfig.Squad is not null)
                            folderConfig.EnsureSandboxSquad(
                                records.Value.Load(SandboxName.For(folder))?.Squad
                            );
                        var prepared = await copy.Value.PrepareAsync(
                            folderConfig,
                            parseResult.GetValue(force),
                            cancellationToken
                        );
                        if (!await preflight.EnsureReadyAsync(cancellationToken))
                            return 1;
                        var sandbox = SandboxName.For(folder);
                        if (await sbx.FindSandboxAsync(sandbox, cancellationToken) is null)
                            throw new AboxException(
                                $"There is no sandbox for '{folder}'. Run 'abox create' for this folder first; user-secrets never creates or replaces one."
                            );
                        UserSecretsReport report;
                        try
                        {
                            report = await copy.Value.CopyAsync(
                                prepared,
                                sandbox,
                                cancellationToken
                            );
                        }
                        catch (OperationCanceledException)
                        {
                            console.WriteError(
                                "User-secret copying was cancelled. Completed files may remain replaced; retry 'abox user-secrets' for this folder. No cross-store rollback is attempted."
                            );
                            throw;
                        }
                        if (report.SafetyOverride)
                            console.WriteLine(
                                "Warning: Safety override used (--force): selected original values matched the global deny list; copy completed."
                            );
                        foreach (var store in report.Stores)
                        {
                            console.WriteLine(
                                $"Copied user-secret store '{store.Id}': {store.KeyCount} keys."
                            );
                            if (store.RewrittenKeyCount > 0)
                                console.WriteLine(
                                    $"Rewrote user-secret store '{store.Id}': {store.RewrittenKeyCount} keys ({string.Join(", ", store.RewrittenKeys.Select(key => System.Text.Json.JsonSerializer.Serialize(key)))})."
                                );
                        }
                        if (report.Stores.Count == 0)
                            console.WriteLine("No user-secret stores selected; nothing copied.");
                        return 0;
                    },
                    console
                )
        );
    }
}

public static class UserSecretsFeature
{
    public static IServiceCollection AddUserSecretsFeature(this IServiceCollection services) =>
        services.AddSingleton<Command, UserSecretsCommand>();
}
