using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Tests;

// The approved process seam executes the actual captured receiver against a
// disposable application-user filesystem, not a second implementation of it.
internal sealed class ShellSandbox : IProcessRunner, IDisposable
{
    private static readonly Lazy<string?> Shell = new(FindShell);
    private readonly TempDirectory root;
    private readonly bool appData;
    public SbxClient Client { get; }
    public string RootPath => root.Path;
    public string SecretsRoot { get; }
    public string StorePath => Path.Combine(SecretsRoot, "synthetic", "secrets.json");
    public Func<string, string>? Frame { get; set; }
    public string BeforeReceiver { get; set; } = "";

    public ShellSandbox(bool appData)
    {
        if (Shell.Value is null)
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value
                    + "Receiver tests require a non-root POSIX shell with GNU coreutils (Git Bash on Windows, discovered from git on PATH)."
            );
        root = new TempDirectory();
        this.appData = appData;
        root.CreateDirectory("appdata");
        root.CreateDirectory("home");
        SecretsRoot = Path.Combine(
            root.Path,
            appData ? "appdata/Microsoft/UserSecrets" : "home/.microsoft/usersecrets"
        );
        Client = new SbxClient(this, AboxPaths.For(root.Path, root.Path));
    }

    public Task<ProcessResult> RunUntilReadyAsync(
        ProcessRequest request,
        Func<CancellationToken, Task> confirmReadiness,
        CancellationToken cancellationToken
    ) => throw new InvalidOperationException("Receiver fixtures must not start a desktop.");

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        var start = request.Arguments.ToList().IndexOf("-c");
        var script =
            BeforeReceiver + (appData ? "" : "unset APPDATA\n") + request.Arguments[start + 1];
        return new ProcessRunner().RunAsync(
            new ProcessRequest(
                Shell.Value!,
                ["-c", script, .. request.Arguments.Skip(start + 2)],
                Environment: ShellEnvironment,
                StandardInput: request.StandardInput is { } frame
                    ? Frame?.Invoke(frame) ?? frame
                    : null,
                OutputPolicy: request.OutputPolicy
            ),
            cancellationToken
        );
    }

    public Task<ProcessResult> RunShellAsync(string script) =>
        new ProcessRunner().RunAsync(
            new ProcessRequest(
                Shell.Value!,
                ["-c", script],
                WorkingDirectory: root.Path,
                Environment: ShellEnvironment,
                StandardInput: ""
            ),
            CancellationToken.None
        );

    private Dictionary<string, string> ShellEnvironment =>
        new()
        {
            ["HOME"] = UnixPath(Path.Combine(root.Path, "home")),
            ["APPDATA"] = UnixPath(Path.Combine(root.Path, "appdata")),
        };

    private static string? FindShell()
    {
        IEnumerable<string> candidates = ["sh"];
        if (OperatingSystem.IsWindows())
            candidates = TestShell
                .GitDirectories(StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(directory =>
                    new[]
                    {
                        directory,
                        Path.Combine(directory, ".."),
                        Path.Combine(directory, "../.."),
                    }
                )
                .SelectMany(directory =>
                    new[]
                    {
                        Path.Combine(directory, "bin/bash.exe"),
                        Path.Combine(directory, "usr/bin/bash.exe"),
                    }
                )
                .Select(Path.GetFullPath)
                .Where(File.Exists)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                var result = new ProcessRunner()
                    .RunAsync(
                        new ProcessRequest(
                            candidate,
                            [
                                "-c",
                                "command -v stat mktemp sha256sum mv wc tr cut grep id chmod cat mkdir rm >/dev/null && [ \"$(id -u)\" -ne 0 ] && stat -c %u . >/dev/null 2>&1",
                            ],
                            StandardInput: ""
                        ),
                        timeout.Token
                    )
                    .GetAwaiter()
                    .GetResult();
                if (result.Succeeded)
                    return candidate;
            }
            catch (Exception exception)
                when (exception is AboxException or OperationCanceledException)
            {
                // Report an explicit test skip if no compatible application-user shell is available.
            }
        return null;
    }

    private static string UnixPath(string path) =>
        OperatingSystem.IsWindows()
            ? "/" + char.ToLowerInvariant(path[0]) + path[2..].Replace('\\', '/')
            : path;

    public void Dispose() => root.Dispose();
}
