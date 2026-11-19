using Abox.Infrastructure;

namespace Abox.Tests;

/// <summary>Local transport for the existing opt-in, non-root disposable Linux fixture.</summary>
internal sealed class DisposableCertificateRuntime : IDisposable
{
    private readonly ProcessRunner local = new();
    private readonly string sandbox;

    private DisposableCertificateRuntime(string sandbox, string runtime)
    {
        this.sandbox = sandbox;
        RuntimePath = runtime;
    }

    internal string RuntimePath { get; }
    internal string? CallerNodeTrust { get; set; }

    internal static async Task<DisposableCertificateRuntime> Create(string sandbox)
    {
        if (
            !OperatingSystem.IsLinux()
            || !File.Exists("/.dockerenv")
            || Environment.GetEnvironmentVariable("ABOX_DISPOSABLE_RECEIVER_CHECKS") != "1"
            || Environment.UserName == "root"
        )
            throw new InvalidOperationException(
                Xunit.v3.DynamicSkipToken.Value + "Requires the non-root disposable Linux runner."
            );
        var identity = await new ProcessRunner().RunAsync(
            new ProcessRequest("id", ["-u"]),
            CancellationToken.None
        );
        var uid = identity.StandardOutput.Trim();
        uid.Should().MatchRegex("^[1-9][0-9]{0,9}$");
        var runtime = "/tmp/abox-https-" + uid;
        Directory
            .Exists(runtime)
            .Should()
            .BeFalse("the disposable must have no previous HTTPS state");
        return new DisposableCertificateRuntime(sandbox, runtime);
    }

    internal Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken token)
    {
        // Replace only sbx transport, executing the exact production argv/source.
        // Never install root trust, even in an explicitly disposable fixture.
        if (request.Arguments.Contains("root"))
            return Task.FromResult(new ProcessResult(0, "", ""));
        var arguments = request.Arguments.SkipWhile(arg => arg != sandbox).Skip(1).ToArray();
        return local.RunAsync(
            request with
            {
                FileName = arguments[0],
                Arguments = arguments[1..],
                AttachConsole = false,
                OutputPolicy = ProcessOutputPolicy.CapturedSensitive,
                Environment = CallerNodeTrust is null
                    ? request.Environment
                    : new Dictionary<string, string> { ["NODE_EXTRA_CA_CERTS"] = CallerNodeTrust },
            },
            token
        );
    }

    public void Dispose()
    {
        // Create refused pre-existing state: this fixture owns only this synthetic runtime.
        if (Directory.Exists(RuntimePath))
            Directory.Delete(RuntimePath, true);
    }
}
