namespace Abox.Infrastructure;

/// <summary>
/// The terminal abox talks to. Abstracted so commands can be tested without a console.
/// </summary>
public interface IConsole
{
    /// <summary>True when a human can answer a question.</summary>
    bool IsInteractive { get; }

    void WriteLine(string message = "");

    void WriteError(string message);

    /// <summary>Asks a yes/no question. Returns false when the answer is no or unreadable.</summary>
    bool Confirm(string question);

    /// <summary>
    /// Reads a value the user should not see, for example a secret. Returns null when
    /// there is nothing to read.
    /// </summary>
    string? ReadSecret(string prompt);
}
