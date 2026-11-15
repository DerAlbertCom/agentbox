using System.Globalization;
using System.Text.Json;
using Abox.Infrastructure;

namespace Abox.Sbx;

public sealed partial class SbxClient
{
    public async Task<IReadOnlyList<int>> GetUserSecretsHostPortsAsync(
        string sandbox,
        CancellationToken cancellationToken
    )
    {
        try
        {
            using var inventory = await ReadUserSecretsPolicyAsync(
                ["policy", "ls", sandbox, "--type", "network", "--json"],
                cancellationToken
            );
            var candidates = UserSecretsNetworkPolicy.Ports(inventory.RootElement, sandbox);
            var authorized = new List<int>();
            foreach (var port in candidates)
            {
                var target = "localhost:" + port.ToString(CultureInfo.InvariantCulture);
                using var check = await ReadUserSecretsPolicyAsync(
                    [
                        "policy",
                        "check",
                        "network",
                        "--sandbox",
                        sandbox,
                        "--protocol",
                        "tcp",
                        "--json",
                        target,
                    ],
                    cancellationToken
                );
                if (UserSecretsNetworkPolicy.IsAllowed(check.RootElement, sandbox, target))
                    authorized.Add(port);
            }
            return authorized.AsReadOnly();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AboxException(
                "Cannot discover authorized user-secret host ports. Check 'sbx policy ls <sandbox> --type network --json' and 'sbx policy check network --sandbox <sandbox> --protocol tcp --json localhost:<port>' for complete, valid sandbox policy metadata; fix the runtime and retry. No policy was changed; child diagnostics were suppressed."
            );
        }
    }

    private async Task<JsonDocument> ReadUserSecretsPolicyAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = await runner.RunAsync(
            new ProcessRequest(
                ExecutableName,
                arguments,
                OutputPolicy: ProcessOutputPolicy.CapturedSensitive
            ),
            cancellationToken
        );
        cancellationToken.ThrowIfCancellationRequested();
        if (!result.Succeeded)
            throw new InvalidOperationException();
        return JsonDocument.Parse(result.StandardOutput);
    }
}
