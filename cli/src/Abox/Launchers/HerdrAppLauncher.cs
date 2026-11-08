using System.Text.Json;
using Abox.Infrastructure;

namespace Abox.Launchers;

/// <summary>
/// The launcher that opens a space in the Herdr desktop app. The Herdr app runs the agents
/// in terminals that stay alive in <c>hc</c>, so they keep running when the window closes.
/// </summary>
public sealed class HerdrAppLauncher : ILauncher
{
    private const string AppProgram = "herdr-app";
    private const string CoreProgram = "hc";

    private readonly IProcessRunner _runner;

    public HerdrAppLauncher(IProcessRunner runner) => _runner = runner;

    public string Id => "herdr-app";

    public string Summary => "A space in the Herdr desktop app";

    public IReadOnlyList<HostRequirement> HostRequirements =>
        [
            new(AppProgram, "Install the Herdr desktop app from https://herdr.dev."),
            new(CoreProgram, "Install herdr-core: it comes with the Herdr desktop app."),
            new(
                "abox",
                "abox must be on PATH: the Herdr app calls 'abox pipe' to reach the sandbox."
            ),
        ];

    public IReadOnlyList<string> SandboxRequirements => [CoreProgram];

    public async Task<int> RunAsync(LauncherContext context, CancellationToken cancellationToken)
    {
        if (!await MachineExistsAsync(context.SandboxName, cancellationToken))
        {
            await AddMachineAsync(context, cancellationToken);
        }

        await StartAppAsync(cancellationToken);

        var arguments = new List<string>
        {
            "do",
            "space.new",
            "--name",
            Path.GetFileName(
                context.Location.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar
                )
            ),
            "--folder",
            context.WorkspacePath,
            "--machine",
            context.SandboxName,
        };

        if (context.Argv.Count > 0)
        {
            arguments.Add("--argv");
            arguments.Add(JsonSerializer.Serialize(context.Argv));
        }

        var result = await _runner.RunAsync(
            new ProcessRequest(AppProgram, arguments, AttachConsole: true),
            cancellationToken
        );

        if (!result.Succeeded)
        {
            throw new AboxException(
                $"'{AppProgram} do space.new' failed with exit code {result.ExitCode}."
            );
        }

        return 0;
    }

    private async Task<bool> MachineExistsAsync(
        string sandboxName,
        CancellationToken cancellationToken
    )
    {
        var result = await _runner.RunAsync(
            new ProcessRequest(CoreProgram, ["machine", "ls", "--json"]),
            cancellationToken
        );

        if (!result.Succeeded)
        {
            return false;
        }

        // The shape of the listing is not known here, so the name is looked for in the
        // output instead of in a fixed place.
        return result.StandardOutput.Contains(sandboxName, StringComparison.Ordinal);
    }

    private async Task AddMachineAsync(LauncherContext context, CancellationToken cancellationToken)
    {
        var via = $"abox pipe {context.SandboxName} -- sh -c";
        var result = await _runner.RunAsync(
            new ProcessRequest(
                CoreProgram,
                ["machine", "add", "--via", via, "--name", context.SandboxName]
            ),
            cancellationToken
        );

        if (!result.Succeeded)
        {
            var message = result.StandardError.Trim();
            throw new AboxException(
                $"'{CoreProgram} machine add' failed with exit code {result.ExitCode}."
                    + (message.Length > 0 ? $" {message}" : string.Empty)
            );
        }
    }

    private async Task StartAppAsync(CancellationToken cancellationToken)
    {
        if (await AppIsRunningAsync(cancellationToken))
        {
            return;
        }

        // The app keeps running when it is started on its own.
        await _runner.RunAsync(
            new ProcessRequest(AppProgram, [], AttachConsole: false),
            cancellationToken
        );
    }

    private async Task<bool> AppIsRunningAsync(CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync(
            new ProcessRequest(AppProgram, ["status"]),
            cancellationToken
        );

        if (!result.Succeeded)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(result.StandardOutput);
            return !document.RootElement.TryGetProperty("errors", out var errors)
                || errors.ValueKind != JsonValueKind.Array
                || errors.GetArrayLength() == 0;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
