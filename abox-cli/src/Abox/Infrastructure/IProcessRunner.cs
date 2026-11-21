namespace Abox.Infrastructure;

/// <summary>
/// Starts an external program. Arguments are always passed as a list, never as a shell string.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);

    /// <summary>
    /// Owns a desktop child until readiness is confirmed, then releases it without
    /// waiting for its event loop to exit. Failure/cancellation kills only the child
    /// started here. Requires normal inherited streams and no supplied stdin.
    /// The caller must bound readiness and honor the supplied cancellation token.
    /// </summary>
    Task<ProcessResult> RunUntilReadyAsync(
        ProcessRequest request,
        Func<CancellationToken, Task> confirmReadiness,
        CancellationToken cancellationToken
    );
}
