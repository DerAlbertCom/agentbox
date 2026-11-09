using Abox.Build;
using Abox.Infrastructure;
using NSubstitute;

namespace Abox.Tests;

internal static class LocalWorkloadTest
{
    public static readonly string ImageId = "sha256:" + new string('a', 64);

    public static IProcessRunner Runner()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => Result(call.Arg<ProcessRequest>()));
        return runner;
    }

    public static ProcessResult Result(ProcessRequest request) =>
        new(
            0,
            request.Arguments.Contains("{{.Id}}") ? ImageId
                : request.Arguments.Contains("ls")
                    ? "{\"Name\":\"test-context\",\"Driver\":\"docker\"}"
                : request.Arguments.SequenceEqual(new[] { "context", "show" }) ? "test-context"
                : "",
            ""
        );

    public static LocalWorkloadImages Images(IProcessRunner? runner = null)
    {
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists("docker").Returns(true);
        return new LocalWorkloadImages(programs, runner ?? Runner(), new TestConsole());
    }

    public static LocalWorkloadImage Artifact(TempDirectory root)
    {
        root.File(
            "source/workload.yaml",
            "# syntax=docker/sandbox-kit:3\nschemaVersion: \"3\"\nkind: workload\n"
        );
        return Images()
            .BuildAsync(
                Path.Combine(root.Path, "source"),
                Path.Combine(root.Path, "runtime", "workload"),
                CancellationToken.None
            )
            .GetAwaiter()
            .GetResult();
    }
}
