using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Tests;

internal sealed class GatedUserSecretsPolicyRunner : IProcessRunner, IDisposable
{
    private readonly TempDirectory root = new();
    private readonly Channel<int> started = Channel.CreateUnbounded<int>();
    private readonly Channel<int> cancelled = Channel.CreateUnbounded<int>();
    private readonly Channel<int> finished = Channel.CreateUnbounded<int>();
    private readonly ConcurrentDictionary<int, TaskCompletionSource<ProcessResult>> checks = new();
    private readonly TaskCompletionSource cleanup = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly string inventory;
    private int active;
    private int maximumActive;

    internal GatedUserSecretsPolicyRunner(string sandbox, params string[] resources)
    {
        Sandbox = sandbox;
        inventory = Inventory(sandbox, resources);
        Client = new SbxClient(this, AboxPaths.For(root.Path, root.Path));
    }

    internal string Sandbox { get; }
    internal SbxClient Client { get; }
    internal bool HoldCancellationCleanup { get; init; }
    internal ConcurrentQueue<ProcessRequest> Requests { get; } = new();
    internal int Active => Volatile.Read(ref active);
    internal int MaximumActive => Volatile.Read(ref maximumActive);

    internal Task<IReadOnlyList<int>> ReadAsync(CancellationToken token = default) =>
        Client.GetUserSecretsHostPortsAsync(Sandbox, token);

    internal async Task<int> NextStartedAsync() =>
        await started.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

    internal async Task<int> NextCancelledAsync() =>
        await cancelled.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

    internal async Task<int> NextFinishedAsync() =>
        await finished.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15));

    internal void Complete(int port, bool allowed = true) =>
        CompleteRaw(port, new ProcessResult(0, Authorization(Sandbox, port, allowed), ""));

    internal void CompleteRaw(int port, ProcessResult result) => checks[port].SetResult(result);

    internal void Fail(int port) =>
        checks[port]
            .SetException(new InvalidOperationException("synthetic-password 123456789 true"));

    internal void ReleaseCleanup() => cleanup.TrySetResult();

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken token)
    {
        Requests.Enqueue(request);
        if (request.Arguments[1] == "ls")
            return new ProcessResult(0, inventory, "");

        var port = int.Parse(request.Arguments[^1].AsSpan("localhost:".Length));
        var gate = new TaskCompletionSource<ProcessResult>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        checks.TryAdd(port, gate).Should().BeTrue("each normalized candidate is checked once");
        var count = Interlocked.Increment(ref active);
        int previous;
        do
        {
            previous = Volatile.Read(ref maximumActive);
        } while (
            count > previous
            && Interlocked.CompareExchange(ref maximumActive, count, previous) != previous
        );
        started.Writer.TryWrite(port);
        try
        {
            return await gate.Task.WaitAsync(token);
        }
        catch (OperationCanceledException)
        {
            cancelled.Writer.TryWrite(port);
            if (HoldCancellationCleanup)
                await cleanup.Task;
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref active);
            finished.Writer.TryWrite(port);
        }
    }

    internal static string Inventory(string sandbox, params string[] resources) =>
        JsonSerializer.Serialize(
            new
            {
                rules = new[]
                {
                    new
                    {
                        name = "kit:" + sandbox,
                        scope = "sandbox:" + sandbox,
                        applies_to = "sandbox:" + sandbox,
                        resource_type = "network",
                        decision = "allow",
                        origin = "scoped",
                        layer = "local",
                        status = "active",
                        editable = false,
                        sandbox_id = sandbox,
                        provenance = new { created_via = "provisioned", source = "sandboxes" },
                        actions = new[] { "net:connect:tcp" },
                        resources,
                    },
                },
            }
        );

    internal static string Authorization(string sandbox, int port, bool allowed = true) =>
        JsonSerializer.Serialize(
            new
            {
                action = "net:connect:tcp",
                allowed,
                context = "sandbox:" + sandbox,
                resource_type = "net:domain",
                resource_value = "localhost:" + port,
                target = "localhost:" + port,
                type = "network",
            }
        );

    public void Dispose()
    {
        ReleaseCleanup();
        root.Dispose();
    }
}
