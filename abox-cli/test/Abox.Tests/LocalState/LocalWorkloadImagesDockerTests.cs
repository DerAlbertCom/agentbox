using Abox.Infrastructure;
using Abox.LocalState;
using NSubstitute;

namespace Abox.Tests;

[Collection("Docker build environment")]
public sealed class LocalWorkloadImagesDockerTests : IDisposable
{
    private readonly LocalWorkloadImagesFixture _fixture = new();
    private IProcessRunner _runner => _fixture.Runner;

    public void Dispose() => _fixture.Dispose();

    private Task<LocalWorkloadImage> BuildAsync(string runtime) => _fixture.BuildAsync(runtime);

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
}
