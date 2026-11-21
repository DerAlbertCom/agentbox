namespace Abox.Infrastructure;

using System.Text;

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

    public int? Choose(string question, IReadOnlyList<string> choices)
    {
        Console.Out.WriteLine(question);
        for (var index = 0; index < choices.Count; index++)
        {
            Console.Out.WriteLine($"  {index + 1}. {choices[index]}");
        }

        Console.Out.Write("Number? ");
        var answer = Console.ReadLine()?.Trim();
        if (!int.TryParse(answer, out var pick) || pick < 1 || pick > choices.Count)
        {
            return null;
        }

        return pick - 1;
    }

    public string? ReadSecret(string prompt)
    {
        Console.Out.Write(prompt);

        if (Console.IsInputRedirected)
        {
            var line = Console.In.ReadLine();
            Console.Out.WriteLine();
            return line;
        }

        var value = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0)
                {
                    value.Remove(value.Length - 1, 1);
                }

                continue;
            }

            if (!char.IsControl(key.KeyChar))
            {
                value.Append(key.KeyChar);
            }
        }

        Console.Out.WriteLine();
        return value.ToString();
    }
}
