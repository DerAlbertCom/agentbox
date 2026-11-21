using System.Text.Json;
using Abox.Infrastructure;

namespace Abox.UserSecrets;

/// <summary>Value-free store summaries shared by automatic and on-demand copying.</summary>
internal static class UserSecretsReportWriter
{
    internal static void WriteStores(UserSecretsReport report, IConsole console)
    {
        foreach (var store in report.Stores)
        {
            console.WriteLine($"Copied user-secret store '{store.Id}': {store.KeyCount} keys.");
            if (store.RewrittenKeyCount > 0)
                console.WriteLine(
                    $"Rewrote user-secret store '{store.Id}': {store.RewrittenKeyCount} keys ({string.Join(", ", store.RewrittenKeys.Select(key => JsonSerializer.Serialize(key)))})."
                );
        }
    }
}
