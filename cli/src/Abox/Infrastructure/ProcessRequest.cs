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
public sealed record ProcessRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    bool AttachConsole = false,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null,
    string? StandardInput = null
);
