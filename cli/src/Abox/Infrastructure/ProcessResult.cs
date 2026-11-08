namespace Abox.Infrastructure;

/// <summary>
/// What a finished process left behind.
/// </summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, string StandardError)
{
    public bool Succeeded => ExitCode == 0;
}
