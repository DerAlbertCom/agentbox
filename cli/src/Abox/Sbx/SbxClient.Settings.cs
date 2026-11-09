using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Abox.Infrastructure;

namespace Abox.Sbx;

public sealed partial class SbxClient
{
    private const string AllowedSourcesKey = "kit.allowedSources";

    /// <summary>Preserves the evaluated allowlist, adding only the requested repository prefix.</summary>
    public Task EnsureKitSourceAllowedAsync(
        string repositoryPrefix,
        CancellationToken cancellationToken
    )
    {
        if (!Regex.IsMatch(repositoryPrefix, @"\A[a-z0-9.-]+/[a-z0-9._-]+/\z"))
        {
            throw new AboxException(
                "Kit source trust requires a repository-scoped prefix, without wildcards."
            );
        }

        // sbx has no atomic append. Serialize abox processes' read/merge/write operations.
        // The mutex must be acquired and released on the same thread, without awaits.
        return Task.Run(
            () =>
            {
                using var mutex = CreateAllowedSourcesMutex();
                var acquired = false;
                try
                {
                    while (!acquired)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            acquired = mutex.WaitOne(TimeSpan.FromMilliseconds(100));
                        }
                        catch (AbandonedMutexException)
                        {
                            acquired = true;
                        }
                    }

                    EnsureKitSourceAllowedCoreAsync(repositoryPrefix, cancellationToken)
                        .GetAwaiter()
                        .GetResult();
                }
                finally
                {
                    if (acquired)
                    {
                        mutex.ReleaseMutex();
                    }
                }
            },
            cancellationToken
        );
    }

    private async Task EnsureKitSourceAllowedCoreAsync(
        string repositoryPrefix,
        CancellationToken cancellationToken
    )
    {
        var (sources, source) = await ReadAllowedSourcesAsync(cancellationToken);
        if (CoversRepository(sources, repositoryPrefix))
        {
            return;
        }

        if (source == "envvar")
        {
            throw new AboxException(
                $"Cannot allow kit repository '{repositoryPrefix}': '{AllowedSourcesKey}' is controlled by an environment variable. Ask your administrator to allow this repository; no sandbox was replaced."
            );
        }

        var updated = sources.Append(repositoryPrefix).ToArray();
        var result = await runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                ["settings", "set", AllowedSourcesKey, JsonSerializer.Serialize(updated)]
            ),
            cancellationToken
        );
        if (!result.Succeeded)
        {
            throw new AboxException(
                $"Cannot allow kit repository '{repositoryPrefix}': 'sbx settings set {AllowedSourcesKey}' failed: {Describe(result)}. Administrator constraints must permit this repository; no sandbox was replaced."
            );
        }

        // Evaluated settings may take a few seconds to pick up a user override.
        for (var attempt = 0; attempt < 11; attempt++)
        {
            var (effective, _) = await ReadAllowedSourcesAsync(cancellationToken);
            if (
                sources.All(entry => effective.Contains(entry, StringComparer.Ordinal))
                && CoversRepository(effective, repositoryPrefix)
            )
            {
                return;
            }

            if (attempt < 10)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            }
        }

        throw new AboxException(
            $"The update of '{AllowedSourcesKey}' did not preserve the allowlist and allow '{repositoryPrefix}' in the evaluated settings. Check environment overrides, administrator policy and concurrent settings changes; no sandbox was replaced."
        );
    }

    private async Task<(string[] Sources, string Source)> ReadAllowedSourcesAsync(
        CancellationToken cancellationToken
    )
    {
        var result = await runner.RunAsync(
            new ProcessRequest(ExecutableName, ["settings", "get", AllowedSourcesKey, "--json"]),
            cancellationToken
        );
        if (!result.Succeeded)
        {
            throw new AboxException(
                $"'sbx settings get {AllowedSourcesKey}' failed: {Describe(result)}. No sandbox was replaced."
            );
        }

        using var document = Deserialize<JsonDocument>(
            result.StandardOutput,
            $"sbx settings get {AllowedSourcesKey}"
        );
        var record = document.RootElement;
        if (
            record.ValueKind != JsonValueKind.Object
            || !record.TryGetProperty("key", out var key)
            || key.ValueKind != JsonValueKind.String
            || key.GetString() != AllowedSourcesKey
            || !record.TryGetProperty("type", out var type)
            || type.ValueKind != JsonValueKind.String
            || type.GetString() != "json"
            || !record.TryGetProperty("source", out var source)
            || source.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(source.GetString())
            || !record.TryGetProperty("value", out var value)
            || value.ValueKind != JsonValueKind.Array
            || value
                .EnumerateArray()
                .Any(entry =>
                    entry.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(entry.GetString())
                )
        )
        {
            throw new AboxException(
                $"Cannot read '{AllowedSourcesKey}': expected a settings record with a JSON array of nonempty source prefixes. No sandbox was replaced."
            );
        }

        return (
            value.EnumerateArray().Select(entry => entry.GetString()!).ToArray(),
            source.GetString()!
        );
    }

    private static bool CoversRepository(IEnumerable<string> sources, string repositoryPrefix) =>
        sources.Any(entry =>
            entry == "*"
            || (
                repositoryPrefix.StartsWith(entry, StringComparison.Ordinal)
                && (
                    entry.EndsWith('/')
                    || entry.Length == repositoryPrefix.Length
                    || repositoryPrefix[entry.Length] == '/'
                )
            )
        );

    private static Mutex CreateAllowedSourcesMutex()
    {
        try
        {
            // CurrentUserOnly restricts access but does not disambiguate Windows object names.
            var user = $"{Environment.UserDomainName}\0{Environment.UserName}";
            var identityHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user)));
            return new Mutex(
                $"abox-sbx-kit-allowedSources-{identityHash}",
                new NamedWaitHandleOptions { CurrentUserOnly = true, CurrentSessionOnly = false }
            );
        }
        catch (Exception exception)
            when (exception
                    is UnauthorizedAccessException
                        or IOException
                        or WaitHandleCannotBeOpenedException
            )
        {
            throw new AboxException(
                $"Cannot lock updates to '{AllowedSourcesKey}'. Close other abox processes and check OS permissions for user-scoped synchronization objects, then retry. No settings were changed and no sandbox was replaced.",
                exception
            );
        }
    }
}
