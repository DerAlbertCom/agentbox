using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Sbx;

namespace Abox.Tests;

public sealed class UserSecretsSensitivePolicyProcessTests
{
    [Theory]
    [InlineData(false, "success")]
    [InlineData(true, "success")]
    [InlineData(false, "malformed")]
    [InlineData(true, "malformed")]
    [InlineData(false, "exit")]
    [InlineData(true, "exit")]
    public async Task ConcurrentPolicyChildrenKeepMetadataInternalEvenOnFailure(
        bool verbose,
        string outcome
    )
    {
        // Arrange
        using var root = new TempDirectory();
        var console = new TestConsole();
        var output = new ProcessOutput(console) { Verbose = verbose };
        var runner = new EchoPolicyRunner(new ProcessRunner(output), outcome);
        var client = new SbxClient(runner, AboxPaths.For(root.Path, root.Path));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // Act
        var read = client.GetUserSecretsHostPortsAsync("synthetic-sandbox", cancellation.Token);
        await runner.AllChecksStarted.Task.WaitAsync(TimeSpan.FromSeconds(20));

        // Assert
        read.IsCompleted.Should().BeFalse("the four process completions are gated");
        console.AllText.Should().BeEmpty();

        // Act
        runner.Release.TrySetResult();

        // Assert
        if (outcome == "success")
            (await read).Should().Equal(8001, 8003, 8004);
        else
        {
            var action = async () => await read;
            var exception = (await action.Should().ThrowAsync<AboxException>()).Which;
            exception.Message.Should().Contain("child diagnostics were suppressed");
            exception
                .ToString()
                .Should()
                .NotContain("synthetic-password")
                .And.NotContain("123456789");
            exception.InnerException.Should().BeNull();
        }
        console
            .AllText.Should()
            .BeEmpty("neither policy metadata nor raw child diagnostics belong on the console");
    }

    private sealed class EchoPolicyRunner(ProcessRunner processes, string outcome) : IProcessRunner
    {
        private int started;
        internal TaskCompletionSource AllChecksStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken token)
        {
            var inventory = request.Arguments[1] == "ls";
            var port = inventory ? 0 : int.Parse(request.Arguments[^1].AsSpan("localhost:".Length));
            var stdout = inventory
                ? GatedUserSecretsPolicyRunner.Inventory(
                    "synthetic-sandbox",
                    "localhost:8001",
                    "localhost:8002",
                    "localhost:8003",
                    "localhost:8004"
                )
                : GatedUserSecretsPolicyRunner
                    .Authorization("synthetic-sandbox", port, allowed: port != 8002)
                    .Replace("}", ",\"diagnostic\":\"synthetic-password 123456789 true\"}");
            if (port == 8002 && outcome == "malformed")
                stdout = "synthetic-password 123456789 true";
            var exitCode = port == 8002 && outcome == "exit" ? 9 : 0;
            var child = processes.RunAsync(
                new ProcessRequest(
                    OperatingSystem.IsWindows() ? "powershell" : "sh",
                    OperatingSystem.IsWindows()
                        ?
                        [
                            "-NoProfile",
                            "-NonInteractive",
                            "-Command",
                            $"[Console]::Out.WriteLine('{stdout}'); [Console]::Error.WriteLine('synthetic-password 123456789 true'); exit {exitCode}",
                        ]
                        :
                        [
                            "-c",
                            $"printf '%s\\n' '{stdout}'; printf '%s\\n' 'synthetic-password 123456789 true' >&2; exit {exitCode}",
                        ],
                    OutputPolicy: request.OutputPolicy
                ),
                token
            );
            if (!inventory && Interlocked.Increment(ref started) == 4)
                AllChecksStarted.TrySetResult();
            // Always settle the actual process, even if a sibling cancels before release.
            var result = await child;
            if (!inventory)
                await Release.Task.WaitAsync(token);
            return result;
        }
    }
}
