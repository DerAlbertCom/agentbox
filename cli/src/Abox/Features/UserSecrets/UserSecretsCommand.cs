using System.CommandLine;
using Abox.Configuration;
using Abox.Health;
using Abox.Infrastructure;
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
                        var prepared = await copy.Value.PrepareAsync(
                            folderConfig,
                            parseResult.GetValue(force),
                            cancellationToken
                        );
                        if (prepared.SafetyOverride)
                            console.WriteLine(
                                "Safety override used (--force): only the value deny list was bypassed."
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
                        foreach (var store in report.Stores)
                            console.WriteLine(
                                $"Copied user-secret store '{store.Id}': {store.KeyCount} keys."
                            );
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
