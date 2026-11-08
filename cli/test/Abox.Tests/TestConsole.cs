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

    /// <summary>The values the test hands out, in the order they are asked for.</summary>
    public List<string> Answers { get; } = [];

    /// <summary>The picks the test hands out, one per question, in order.</summary>
    public List<int> Picks { get; } = [];

    private int _next;

    private int _picked;

    public int? Choose(string question, IReadOnlyList<string> choices)
    {
        _output.Add(question);
        return _picked < Picks.Count ? Picks[_picked++] : null;
    }

    public string? ReadSecret(string prompt)
    {
        _output.Add(prompt);
        return _next < Answers.Count ? Answers[_next++] : string.Empty;
    }
}
