using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Abox.Configuration;
using Abox.Sbx;
using Performance40.Policy;

if (args.Length > 1 || (args.Length == 1 && (!int.TryParse(args[0], out var count) || count < 2)))
{
    Console.Error.WriteLine("Usage: Performance40.Policy [repetitions >= 2] (default: 5)");
    return 1;
}
var repetitions = args.Length == 0 ? 5 : int.Parse(args[0], CultureInfo.InvariantCulture);
Console.WriteLine(
    $"# {RuntimeInformation.OSDescription}; {RuntimeInformation.FrameworkDescription}; {RuntimeInformation.ProcessArchitecture}"
);
Console.WriteLine(
    "# Synthetic policy only; no processes, sandbox, discovery, stores or transfer. Build/startup excluded."
);
Console.WriteLine(
    "# Same 14 candidates; inventory delay 75 ms; each check delay 125 ms; warm up both modes once; alternate order."
);
Console.WriteLine(
    "# Serial baseline serializes the substituted process-runner boundary; both modes use the current strict SbxClient."
);
await Measure(1);
await Measure(4);
Console.WriteLine(
    "repetition,check_concurrency,candidates,authorized,equivalent,inventory_ms,authorization_and_parsing_ms,total_policy_ms,result_sha256"
);
for (var repetition = 1; repetition <= repetitions; repetition++)
{
    foreach (var concurrency in repetition % 2 == 1 ? new[] { 1, 4 } : new[] { 4, 1 })
    {
        var result = await Measure(concurrency);
        Console.WriteLine(
            FormattableString.Invariant(
                $"{repetition},{result.Concurrency},14,11,true,{result.Inventory:F2},{result.Authorization:F2},{result.Total:F2},{result.Fingerprint}"
            )
        );
    }
}
return 0;

static async Task<Measurement> Measure(int concurrency)
{
    using var runner = new SyntheticPolicyRunner(concurrency);
    var client = new SbxClient(
        runner,
        AboxPaths.For(AppContext.BaseDirectory, AppContext.BaseDirectory)
    );
    var stopwatch = Stopwatch.StartNew();
    var ports = await client.GetUserSecretsHostPortsAsync(
        "synthetic-sandbox",
        CancellationToken.None
    );
    stopwatch.Stop();
    int[] expected = [8001, 8002, 8003, 8005, 8006, 8007, 8009, 8010, 8011, 8013, 8014];
    if (
        !ports.SequenceEqual(expected)
        || runner.Inventories != 1
        || runner.Checks != 14
        || runner.MaximumActive != concurrency
    )
        throw new InvalidOperationException(
            "Synthetic policy results, coverage or concurrency differed; do not report timings."
        );
    var fingerprint = Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join(",", ports)))
    );
    return new Measurement(
        concurrency,
        runner.InventoryMilliseconds,
        stopwatch.Elapsed.TotalMilliseconds - runner.InventoryMilliseconds,
        stopwatch.Elapsed.TotalMilliseconds,
        fingerprint
    );
}

internal sealed record Measurement(
    int Concurrency,
    double Inventory,
    double Authorization,
    double Total,
    string Fingerprint
);
