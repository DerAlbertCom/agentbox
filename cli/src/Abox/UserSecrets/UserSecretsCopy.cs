using System.Text;
using System.Text.Json;
using Abox.Configuration;
using Abox.Sbx;

namespace Abox.UserSecrets;

/// <summary>Shared on-demand/lifecycle flow. Preparation never contacts or writes a sandbox.</summary>
public sealed partial class UserSecretsCopy(AboxConfig config, UserSecretsHost host, ISbxClient sbx)
{
    public async Task<PreparedUserSecrets> PrepareAsync(
        FolderConfig? folderConfig,
        bool force,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (folderConfig is null)
            return new PreparedUserSecrets([], false);
        if (
            config.UserSecretsDenyList is null
            || config.UserSecretsDenyList.Any(string.IsNullOrWhiteSpace)
        )
            throw new AboxException(
                "Global 'userSecretsDenyList' must be a list of nonempty literal substrings; --force does not bypass invalid configuration."
            );
        var stores = new List<UserSecretSnapshot>();
        foreach (var selection in folderConfig?.UserSecrets ?? [])
        {
            cancellationToken.ThrowIfCancellationRequested();
            string json;
            try
            {
                json = await File.ReadAllTextAsync(
                    host.StorePath(selection.Id),
                    new UTF8Encoding(false, true),
                    cancellationToken
                );
            }
            catch (DecoderFallbackException)
            {
                throw new AboxException(
                    $"User-secret store '{selection.Id}' has invalid UTF-8. Fix the host file; no stores were copied."
                );
            }
            catch (Exception exception)
                when (exception is IOException or UnauthorizedAccessException)
            {
                throw new AboxException(
                    $"Cannot read user-secret store '{selection.Id}'. Check its existence and file permissions on this host."
                );
            }
            stores.Add(UserSecretSnapshot.Parse(selection, json));
        }
        var safetyOverride = false;
        foreach (var store in stores)
        foreach (var (key, value) in store.Values)
            if (
                value is not null
                && config.UserSecretsDenyList.Any(denied =>
                    value.Contains(denied, StringComparison.OrdinalIgnoreCase)
                )
            )
            {
                if (!force)
                    throw new AboxException(
                        $"User-secret store '{store.Id}', key {JsonSerializer.Serialize(key)}, is blocked by the global deny list. No stores were copied. Use --force only if this is intentional."
                    );
                safetyOverride = true;
            }
        return new PreparedUserSecrets(stores, safetyOverride);
    }

    public async Task<UserSecretsReport> CopyAsync(
        PreparedUserSecrets prepared,
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (prepared.Stores.Count == 0)
            return new UserSecretsReport([], prepared.SafetyOverride);
        IReadOnlyList<UserSecretsTargetStore> targetStores;
        try
        {
            targetStores = await PrepareTargetAsync(prepared, sandbox, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AboxException(
                "Cannot prepare user-secret destinations or rewrite policy. No stores were copied. Check 'sbx policy ls <sandbox> --source kit --type network --json' and 'sbx policy check network --sandbox <sandbox> --protocol tcp --json localhost:<port>' for complete, valid metadata, and the application user's absolute HOME/APPDATA, ownership, ordinary paths and shell/coreutils; fix the runtime and retry 'abox user-secrets'. No policy was changed; child diagnostics were suppressed."
            );
        }
        var copied = new List<UserSecretsStoreReport>();
        foreach (var target in targetStores)
        {
            var store = target.Snapshot;
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await sbx.WriteUserSecretsAsync(
                    sandbox,
                    store.Id,
                    // Raw text retains skipped comments; transfer parsed data only.
                    JsonSerializer.Serialize(store.Root),
                    cancellationToken
                );
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                throw new AboxException(
                    $"User-secret transfer failed for store '{store.Id}' after {copied.Count} completed stores. Earlier files may have been replaced; the current result may be unconfirmed. Fix the destination/runtime and retry 'abox user-secrets' for this folder; no cross-store rollback is attempted."
                );
            }
            copied.Add(
                new UserSecretsStoreReport(store.Id, store.KeyCount)
                {
                    RewrittenKeys = target.RewrittenKeys,
                }
            );
        }
        return new UserSecretsReport(copied.AsReadOnly(), prepared.SafetyOverride);
    }
}

/// <summary>Opaque, in-memory validated snapshot; no payload-bearing generated ToString.</summary>
public sealed class PreparedUserSecrets
{
    internal PreparedUserSecrets(IReadOnlyList<UserSecretSnapshot> stores, bool safetyOverride)
    {
        Stores = stores;
        SafetyOverride = safetyOverride;
    }

    internal IReadOnlyList<UserSecretSnapshot> Stores { get; }

    /// <summary>At least one selected original value matched the deny list and was allowed by --force.</summary>
    public bool SafetyOverride { get; }

    public override string ToString() =>
        $"Prepared user secrets ({Stores.Count} stores; values hidden)";
}

public sealed record UserSecretsStoreReport(string Id, int KeyCount)
{
    public IReadOnlyList<string> RewrittenKeys { get; init; } = [];
    public int RewrittenKeyCount => RewrittenKeys.Count;
}

/// <summary>Successful copy summary; SafetyOverride records actual selected-original deny matches allowed by --force.</summary>
public sealed record UserSecretsReport(
    IReadOnlyList<UserSecretsStoreReport> Stores,
    bool SafetyOverride
);
