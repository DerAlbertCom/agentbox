using Abox.Infrastructure;

namespace Abox.Features;

/// <summary>
/// Turns abox errors into exit code 1 and cancellation into exit code 130.
/// Used by command actions and by the entry point, which also guards composition.
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
            return 130;
        }
    }
}
