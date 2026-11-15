using System.Diagnostics;
using System.Text.Json;
using Abox.Infrastructure;

namespace Performance40.Policy;

// Models independent process latency without launching any tool or reading host inputs.
internal sealed class SyntheticPolicyRunner(int concurrency) : IProcessRunner, IDisposable
{
    private readonly SemaphoreSlim slots = new(concurrency);
    private int active;
    private int maximumActive;
    private int checks;
    internal int MaximumActive => maximumActive;
    internal int Inventories { get; private set; }
    internal int Checks => checks;
    internal double InventoryMilliseconds { get; private set; }

    private static readonly string Inventory = JsonSerializer.Serialize(
        new
        {
            rules = new[]
            {
                new
                {
                    name = "kit:synthetic-sandbox",
                    scope = "sandbox:synthetic-sandbox",
                    applies_to = "sandbox:synthetic-sandbox",
                    resource_type = "network",
                    decision = "allow",
                    origin = "scoped",
                    layer = "local",
                    status = "active",
                    editable = false,
                    sandbox_id = "synthetic-sandbox",
                    provenance = new { created_via = "provisioned", source = "sandboxes" },
                    actions = new[] { "net:connect:tcp" },
                    resources = Enumerable
                        .Range(8001, 14)
                        .Reverse()
                        .Select(port => "localhost:" + port)
                        .ToArray(),
                },
            },
        }
    );

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken token)
    {
        if (request.Arguments[1] == "ls")
        {
            var stopwatch = Stopwatch.StartNew();
            Inventories++;
            await Task.Delay(75, token);
            InventoryMilliseconds = stopwatch.Elapsed.TotalMilliseconds;
            return new ProcessResult(0, Inventory, "");
        }

        await slots.WaitAsync(token);
        try
        {
            var count = Interlocked.Increment(ref active);
            int previous;
            do
            {
                previous = Volatile.Read(ref maximumActive);
            } while (
                count > previous
                && Interlocked.CompareExchange(ref maximumActive, count, previous) != previous
            );
            Interlocked.Increment(ref checks);
            await Task.Delay(125, token);
            var target = request.Arguments[^1];
            var port = int.Parse(target.AsSpan("localhost:".Length));
            return new ProcessResult(
                0,
                JsonSerializer.Serialize(
                    new
                    {
                        action = "net:connect:tcp",
                        allowed = port % 4 != 0,
                        context = "sandbox:synthetic-sandbox",
                        resource_type = "net:domain",
                        resource_value = target,
                        target,
                        type = "network",
                    }
                ),
                ""
            );
        }
        finally
        {
            Interlocked.Decrement(ref active);
            slots.Release();
        }
    }

    public void Dispose() => slots.Dispose();
}
