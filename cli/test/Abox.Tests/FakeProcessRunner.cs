using Abox.Infrastructure;

namespace Abox.Tests;

/// <summary>
/// A process runner that answers what the test tells it to, and keeps what it was asked.
/// </summary>
public sealed class FakeProcessRunner : IProcessRunner
{
    /// <summary>The requests it got.</summary>
    public List<ProcessRequest> Requests { get; } = [];

    /// <summary>The results to hand out, in the order the requests come in.</summary>
    public Queue<ProcessResult> Results { get; } = [];

    /// <summary>The result of a request that has no queued answer.</summary>
    public ProcessResult Default { get; set; } = new(0, string.Empty, string.Empty);

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(Results.Count > 0 ? Results.Dequeue() : Default);
    }

    /// <summary>Queues a successful answer with this standard output.</summary>
    public void AnsweredWith(string standardOutput) =>
        Results.Enqueue(new ProcessResult(0, standardOutput, string.Empty));
}
