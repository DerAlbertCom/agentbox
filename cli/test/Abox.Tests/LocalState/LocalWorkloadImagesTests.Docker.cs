using Abox.Infrastructure;
using Abox.LocalState;
using NSubstitute;

namespace Abox.Tests;

[Collection("Docker build environment")]
public sealed partial class LocalWorkloadImagesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("synthetic-github-build-token")]
    public async Task LocalWorkloadsReceiveAnAvailableGitHubTokenWithoutChangingTheirHostBuilder(
        string? token
    )
    {
        // Arrange
        var calls = new List<ProcessRequest>();
        _runner
            .When(runner =>
                runner.RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            )
            .Do(call => calls.Add(call.Arg<ProcessRequest>()));
        var previous = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", token);
        try
        {
            // Act
            await BuildAsync("runtime");

            // Assert
            var build = calls.Single(call => call.Arguments.Contains("--load"));
            if (string.IsNullOrEmpty(token))
            {
                build.Arguments.Should().NotContain("--secret");
            }
            else
            {
                build
                    .Arguments.Should()
                    .ContainInOrder("--secret", "id=github_token,env=GITHUB_TOKEN");
                calls.SelectMany(call => call.Arguments).Should().NotContain(token);
            }
            build
                .Arguments.Take(6)
                .Should()
                .Equal("--context", "test-context", "buildx", "build", "--builder", "test-context");
            calls
                .Should()
                .NotContain(call =>
                    call.Arguments.Contains("--push") || call.Arguments.Contains("pull")
                );
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", previous);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingDockerStopsLocalBuildAndAvailabilityChecksBeforeReadingSources(
        bool checkAvailability
    )
    {
        // Arrange
        var programs = Substitute.For<IHostPrograms>();
        var images = new LocalWorkloadImages(programs, new OciRegistry(_runner), new TestConsole());

        // Act
        Func<Task> action = checkAvailability
            ? () => images.EnsureAvailableAsync(new LocalWorkloadImage(), CancellationToken.None)
            : () => images.BuildAsync("missing-source", "missing-runtime", CancellationToken.None);

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "Local workload images need Docker with Buildx on PATH. Install Docker and run 'abox build' again."
        );
        await _runner
            .DidNotReceive()
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MissingBuildxStopsLocalBuildBeforeContextSelection()
    {
        // Arrange
        _runner
            .RunAsync(
                Arg.Is<ProcessRequest>(request => request.Arguments.Contains("version")),
                Arg.Any<CancellationToken>()
            )
            .Returns(new ProcessResult(17, "", "synthetic buildx failure"));

        // Act
        var action = () => BuildAsync("runtime");

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*Local workload image operation failed*exit 17*No source-build fallback*synthetic buildx failure*"
        );
        await _runner
            .DidNotReceive()
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.Contains("context") || request.Arguments.Contains("--load")
                ),
                Arg.Any<CancellationToken>()
            );
    }
}
