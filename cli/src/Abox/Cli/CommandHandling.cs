using Abox.Infrastructure;

namespace Abox.Cli;

/// <summary>
/// Runs the work of a command and turns an abox error into a message and exit code 1.
/// </summary>
public static class CommandHandling
{
    public static async Task<int> RunAsync(Func<Task<int>> work, IConsole console)
    {
        try
        {
            return await work();
        }
        catch (AboxException exception)
        {
            console.WriteError($"error: {exception.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            console.WriteError("error: the command was cancelled.");
            return 1;
        }
    }
}
