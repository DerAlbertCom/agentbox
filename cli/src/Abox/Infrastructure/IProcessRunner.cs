namespace Abox.Infrastructure;

/// <summary>
/// Starts an external program. Arguments are always passed as a list, never as a shell string.
/// </summary>
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken);
}
