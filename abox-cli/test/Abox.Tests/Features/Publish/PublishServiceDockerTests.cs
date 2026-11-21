using Abox.Features.Publish;
using Abox.Infrastructure;

namespace Abox.Tests;

[Collection("Docker build environment")]
public sealed class PublishServiceDockerTests
{
    private readonly PublishServiceFixture _fixture = new();
    private TestConsole _console => _fixture.Console;
    private List<ProcessRequest> _calls => _fixture.Calls;
    private PublishService _service => _fixture.Service;

    private PublishRequest Request(params string[] squads) => _fixture.Request(squads);

    private void SaveBuild(string squad) => _fixture.SaveBuild(squad);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("synthetic-github-build-token")]
    public async Task OnlyPublishedWorkloadsReceiveAnAvailableGitHubTokenAsAnEnvironmentSecret(
        string? token
    )
    {
        // Arrange
        SaveBuild("ac");
        var previous = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", token);
        try
        {
            // Act
            await _service.PublishAsync(
                Request("ac") with
                {
                    Builder = "publisher",
                },
                CancellationToken.None
            );

            // Assert
            var pushes = _calls.Where(call => call.Arguments.Contains("--push")).ToList();
            var workload = pushes.Single(call =>
                call.Arguments.Any(arg => arg.Contains("/components/workload:"))
            );
            if (string.IsNullOrEmpty(token))
            {
                workload.Arguments.Should().NotContain("--secret");
            }
            else
            {
                workload
                    .Arguments.Should()
                    .ContainInOrder("--secret", "id=github_token,env=GITHUB_TOKEN");
                _calls.SelectMany(call => call.Arguments).Should().NotContain(token);
                _console.AllText.Should().NotContain(token);
            }
            pushes
                .Where(call => call != workload)
                .Should()
                .OnlyContain(call => !call.Arguments.Contains("--secret"));
            pushes
                .Should()
                .OnlyContain(call =>
                    call.Arguments.Contains("publisher")
                    && !call.Arguments.Contains("--context")
                    && !call.Arguments.Contains("--load")
                );
        }
        finally
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", previous);
        }
    }
}
