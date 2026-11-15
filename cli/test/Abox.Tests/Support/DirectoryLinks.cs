using Abox.Infrastructure;

namespace Abox.Tests;

internal sealed class DirectoryLinks : IDisposable
{
    private readonly List<string> links = [];

    internal async Task Add(string link, string target, bool symbolic = false)
    {
        link = Path.GetFullPath(link);
        target = Path.GetFullPath(target);
        try
        {
            if (OperatingSystem.IsWindows() && !symbolic)
            {
                var result = await new ProcessRunner().RunAsync(
                    new ProcessRequest("cmd", ["/c", "mklink", "/J", link, target]),
                    CancellationToken.None
                );
                result.ExitCode.Should().Be(0, result.StandardError);
            }
            else
                Directory.CreateSymbolicLink(link, target);
            links.Add(link);
        }
        catch (Exception exception)
            when (exception is UnauthorizedAccessException or PlatformNotSupportedException
                || (
                    OperatingSystem.IsWindows()
                    && exception is IOException
                    && (exception.HResult & 0xffff) == 1314
                )
            )
        {
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value
                    + "Directory links require native link support and privileges on this host.",
                exception
            );
        }
    }

    public void Dispose()
    {
        for (var index = links.Count - 1; index >= 0; index--)
            if (OperatingSystem.IsWindows())
                Directory.Delete(links[index]);
            else
                File.Delete(links[index]);
    }
}
