namespace Abox.Infrastructure;

/// <summary>
/// A program to start and how to start it.
/// </summary>
/// <param name="FileName">The executable to start.</param>
/// <param name="Arguments">The arguments, passed one by one so no quoting is needed.</param>
/// <param name="AttachConsole">
/// When true the child inherits this process' standard streams, which keeps an
/// interactive terminal interactive. When false the output is captured.
/// </param>
/// <param name="WorkingDirectory">The directory to start in, or null for the current one.</param>
/// <param name="Environment">Extra environment variables for the child.</param>
/// <param name="StandardInput">The text the child reads from its standard input, or null.</param>
/// <param name="OutputPolicy">Sensitive calls discard output/errors and use UTF-8/no-BOM stdin.
/// CapturedSensitive retains metadata for parsing. Both suppress diagnostics and forbid attached streams.</param>
/// <param name="UseUtf8StandardInput">Write supplied stdin as strict UTF-8 without a BOM,
/// independently of console encoding. Does not change output diagnostics or inherited streams.</param>
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    bool AttachConsole = false,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? StandardInput = null,
    ProcessOutputPolicy OutputPolicy = ProcessOutputPolicy.Normal,
    bool UseUtf8StandardInput = false
)
{
    public override string ToString() => "ProcessRequest (arguments, environment and input hidden)";
}

public enum ProcessOutputPolicy
{
    Normal,
    Sensitive,

    // Capture trusted-command metadata for parsing, but never emit raw child diagnostics.
    CapturedSensitive,
}
