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
            var allowed = new bool[candidates.Count];
            using var checks = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var next = -1;

            async Task CheckCandidatesAsync()
            {
                try
                {
                    while (true)
                    {
                        var index = Interlocked.Increment(ref next);
                        if (index >= candidates.Count)
                            return;
                        var target =
                            "localhost:" + candidates[index].ToString(CultureInfo.InvariantCulture);
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
                            checks.Token
                        );
                        allowed[index] = UserSecretsNetworkPolicy.IsAllowed(
                            check.RootElement,
                            sandbox,
                            target
                        );
                    }
                }
                catch
                {
                    await checks.CancelAsync();
                    throw;
                }
            }

            // Await every worker, including cancellation cleanup, before exposing any authorization.
            await Task.WhenAll(
                Enumerable
                    .Range(0, Math.Min(4, candidates.Count))
                    .Select(_ => CheckCandidatesAsync())
            );
            cancellationToken.ThrowIfCancellationRequested();
            return candidates.Where((_, index) => allowed[index]).ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
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
