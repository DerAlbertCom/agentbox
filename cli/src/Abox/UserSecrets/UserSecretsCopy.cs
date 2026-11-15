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
            return new PreparedUserSecrets([], false, force, []);
        // Callers can construct settings directly, without the YAML loader's validation.
        if (
            config.UserSecretsDenyList is null
            || config.UserSecretsDenyList.Any(string.IsNullOrWhiteSpace)
        )
            throw new AboxException(
                "Global 'userSecretsDenyList' must be a list of nonempty literal substrings; --force does not bypass invalid configuration."
            );
        var stores = new List<UserSecretSnapshot>();
        var selections = folderConfig.UserSecrets.Any(selection => selection.IsPrefixPattern)
            ? UserSecretSelection.Expand(folderConfig.UserSecrets, host.StoreIds(cancellationToken))
            : folderConfig.UserSecrets;
        var projects = await ProjectSettings.ReadAsync(
            folderConfig.Location,
            selections,
            cancellationToken
        );
        foreach (var selection in selections)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var consumers = projects
                .Where(project =>
                    project.SelectedSettings.Id.Equals(
                        selection.Id,
                        StringComparison.OrdinalIgnoreCase
                    )
                )
                .ToArray();
            string json;
            try
            {
                json = await File.ReadAllTextAsync(
                    host.StorePath(selection.Id),
                    new UTF8Encoding(false, true),
                    cancellationToken
                );
            }
            catch (Exception exception)
                when (consumers.Any(project => project.HasAppSettingsSource)
                    && exception is FileNotFoundException or DirectoryNotFoundException
                )
            {
                json = "{}";
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
                    $"Cannot read user-secret store '{selection.Id}'. Check its existence and file permissions on this host, or supply project-local appsettings.json/appsettings.development.json beneath this workspace with an unconditional literal UserSecretsId. Computed/imported IDs are not evaluated."
                );
            }
            stores.Add(UserSecretSnapshot.Parse(selection, json));
        }
        var safetyOverride = false;
        foreach (var store in stores)
        foreach (var (key, value) in store.Values)
            safetyOverride |= CheckOriginal(store.Id, key, value, force);
        return new PreparedUserSecrets(stores, safetyOverride, force, projects);
    }

    private bool CheckOriginal(
        string id,
        string key,
        string? value,
        bool force,
        string? project = null
    )
    {
        if (
            value is null
            || !config.UserSecretsDenyList.Any(denied =>
                value.Contains(denied, StringComparison.OrdinalIgnoreCase)
            )
        )
            return false;
        if (!force)
            throw new AboxException(
                $"{DescribeSetting(id, key, project)}, is blocked by the global deny list. No stores were copied. Use --force only if this is intentional."
            );
        return true;
    }

    private static string DescribeSetting(string id, string key, string? project) =>
        $"User-secret store '{id}', key {JsonSerializer.Serialize(key)}{(project is null ? "" : $", project {JsonSerializer.Serialize(project)}")}";

    public async Task<UserSecretsReport> CopyAsync(
        PreparedUserSecrets prepared,
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (prepared.Stores.Count == 0)
            return new UserSecretsReport([], prepared.SafetyOverride);
        var (targetStores, safetyOverride) = await PrepareTargetAsync(
            prepared,
            sandbox,
            cancellationToken
        );
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
        return new UserSecretsReport(copied.AsReadOnly(), safetyOverride);
    }
}

/// <summary>Opaque, in-memory validated snapshot; no payload-bearing generated ToString.</summary>
public sealed class PreparedUserSecrets
{
    internal PreparedUserSecrets(
        IReadOnlyList<UserSecretSnapshot> stores,
        bool safetyOverride,
        bool force,
        IReadOnlyList<ProjectSettings> projects
    )
    {
        Stores = stores;
        SafetyOverride = safetyOverride;
        Force = force;
        Projects = projects;
    }

    internal IReadOnlyList<UserSecretSnapshot> Stores { get; }
    internal bool Force { get; }
    internal IReadOnlyList<ProjectSettings> Projects { get; }

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
