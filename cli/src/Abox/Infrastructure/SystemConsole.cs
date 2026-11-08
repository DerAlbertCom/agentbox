namespace Abox.Infrastructure;

/// <summary>
/// <see cref="IConsole"/> on top of <see cref="System.Console"/>.
/// </summary>
public sealed class SystemConsole : IConsole
{
    public bool IsInteractive => !Console.IsInputRedirected;

    public void WriteLine(string message = "") => Console.Out.WriteLine(message);

    public void WriteError(string message) => Console.Error.WriteLine(message);

    public bool Confirm(string question)
    {
        Console.Out.Write($"{question} [y/N] ");
        var answer = Console.In.ReadLine();
        return answer is not null
            && (
                answer.Trim().Equals("y", StringComparison.OrdinalIgnoreCase)
                || answer.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase)
            );
    }
}
