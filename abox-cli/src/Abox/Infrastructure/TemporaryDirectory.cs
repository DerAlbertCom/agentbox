namespace Abox.Infrastructure;

/// <summary>A temporary working directory whose cleanup failures are warnings.</summary>
public sealed class TemporaryDirectory : IDisposable
{
    private readonly IConsole console;
    private readonly string purpose;

    public TemporaryDirectory(string prefix, string purpose, IConsole console)
    {
        this.console = console;
        this.purpose = purpose;
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            prefix + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            console.WriteError(
                $"warning: could not remove {purpose} directory '{Path}': {exception.Message}"
            );
        }
    }
}
