using Abox.Infrastructure;

namespace Abox.Tests;

/// <summary>
/// A console that keeps what was written to it.
/// </summary>
public sealed class TestConsole : IConsole
{
    private readonly List<string> _output = [];
    private readonly List<string> _errors = [];

    public TestConsole(bool interactive = true, bool answer = true)
    {
        IsInteractive = interactive;
        Answer = answer;
    }

    public bool IsInteractive { get; }

    public bool Answer { get; set; }

    public IReadOnlyList<string> Output => _output;

    public IReadOnlyList<string> Errors => _errors;

    public string AllText => string.Join(Environment.NewLine, _output.Concat(_errors));

    public void WriteLine(string message = "") => _output.Add(message);

    public void WriteError(string message) => _errors.Add(message);

    public bool Confirm(string question)
    {
        _output.Add(question);
        return Answer;
    }
}
