using Abox;
using Abox.Infrastructure;
using Abox.LocalState;
using NSubstitute;

namespace Abox.Tests;

public sealed class LocalWorkloadImagesTests
{
    private readonly TempDirectory _root = new();
    private readonly IProcessRunner _runner = LocalWorkloadTest.Runner();

    [Fact]
    public async Task BuildProducesARealUniqueLocalImageAndAThinV3Kit()
    {
        var image = await BuildAsync("one");
        var next = await BuildAsync("two");

        image.Reference.Should().StartWith("docker.io/library/abox-workload:");
        image.Reference.Should().NotBe(next.Reference);
        image.ImageId.Should().Be(LocalWorkloadTest.ImageId);
        image.DockerContext.Should().Be("test-context");
        File.ReadAllText(Path.Combine(image.RuntimeKitDirectory, "workload.dockerfile"))
            .Should()
            .Be($"FROM {image.Reference}\n");
        File.ReadAllText(Path.Combine(image.RuntimeKitDirectory, "workload.yaml"))
            .Should()
            .Be(File.ReadAllText(Path.Combine(_root.Path, "source", "workload.yaml")));
        await _runner
            .Received(2)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.FileName == "docker"
                    && request.Arguments.Contains("--load")
                    && request.Arguments.Contains(
                        Path.Combine(_root.Path, "source", "workload.yaml")
                    )
                    && request.Arguments.Contains("test-context")
                    && !request.Arguments.Contains("--push")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task CheckingAvailabilityDoesNotBuildOrPull()
    {
        var image = await BuildAsync("one");
        _runner.ClearReceivedCalls();

        await LocalWorkloadTest.Images(_runner).EnsureAvailableAsync(image, CancellationToken.None);

        await _runner.Received(2).RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
        await _runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("build") || request.Arguments.Contains("pull")
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MissingOrReplacedLocalContentRequiresRebuilding(bool missing)
    {
        var image = await BuildAsync("one");
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("{{.Id}}")),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                missing
                    ? new ProcessResult(1, "", "No such image")
                    : new ProcessResult(0, "sha256:" + new string('b', 64), "")
            );

        var action = () =>
            LocalWorkloadTest.Images(_runner).EnsureAvailableAsync(image, CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*abox build*again*");
    }

    [Fact]
    public async Task ChangedRuntimeRecipesAreRejectedBeforeAnyDockerOperation()
    {
        var image = await BuildAsync("one");
        File.AppendAllText(
            Path.Combine(image.RuntimeKitDirectory, "workload.dockerfile"),
            "RUN expensive-install\n"
        );
        _runner.ClearReceivedCalls();

        var action = () =>
            LocalWorkloadTest.Images(_runner).EnsureAvailableAsync(image, CancellationToken.None);

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*runtime kit changed*abox build*"
        );
        await _runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ARemoteBuilderIsNotUsedForHostOnlyImages()
    {
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("ls")),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new ProcessResult(
                    0,
                    "{\"Name\":\"test-context\",\"Driver\":\"docker-container\"}",
                    ""
                )
            );

        var action = () => BuildAsync("one");

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*docker-driver*docker-container*"
        );
        await _runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("--load")),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task SourcesChangedWhileDockerBuildsAreNotRecorded()
    {
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("--load")),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                File.AppendAllText(
                    Path.Combine(_root.Path, "source", "workload.yaml"),
                    "description: changed\n"
                );
                return new ProcessResult(0, "", "");
            });

        var action = () => BuildAsync("one");

        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*sources changed*abox build*"
        );
        Directory.Exists(Path.Combine(_root.Path, "one")).Should().BeFalse();
    }

    private Task<LocalWorkloadImage> BuildAsync(string runtime)
    {
        _root.File(
            "source/workload.yaml",
            "# syntax=docker/sandbox-kit:3\nschemaVersion: \"3\"\nkind: workload\n"
        );
        _root.File("source/workload.dockerfile", "FROM example.com/base:1\nRUN install-tools\n");
        return LocalWorkloadTest
            .Images(_runner)
            .BuildAsync(
                Path.Combine(_root.Path, "source"),
                Path.Combine(_root.Path, runtime),
                CancellationToken.None
            );
    }
}
