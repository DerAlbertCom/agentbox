using System.Text.Json;

namespace Abox.Tests;

public sealed partial class PublishedSquadSourceTests
{
    [Theory]
    [InlineData("/home/agent/.published/sessions", ".published/sessions")]
    [InlineData("/home/agent/.PUBLISHED", ".PUBLISHED")]
    [InlineData("/.published", ".published")]
    [InlineData("/.published/sessions", ".published/sessions")]
    [InlineData("/home/agent", "home/agent")]
    public async Task PublishedStateCannotOverlapInTheSandboxOrOnTheHost(
        string sandboxPath,
        string relativePath
    )
    {
        // Arrange
        SetPublishedState(("/home/agent/.published", ".published"), (sandboxPath, relativePath));

        // Act
        var action = LoadAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage(
            "*republish*No local metadata fallback*not replaced*"
        );
    }

    [Theory]
    [InlineData("/var/CON", "var/CON")]
    [InlineData("/var/cache.", "var/cache.")]
    [InlineData("/var/cache /sessions", "var/cache /sessions")]
    public async Task PublishedStateUsesPortableNamesEvenWhenEveryMountIsDeclared(
        string sandboxPath,
        string relativePath
    )
    {
        // Arrange
        SetPublishedState((sandboxPath, relativePath));

        // Act
        var action = LoadAsync;

        // Assert
        (await action.Should().ThrowAsync<AboxException>()).WithMessage("*republish*not replaced*");
    }

    [Theory]
    [InlineData("/home/agent/.published", ".published")]
    [InlineData("/var/lib/cache", "var/lib/cache")]
    [InlineData("/home/agent", "home/agent")]
    [InlineData("/home/agent2", "home/agent2")]
    public async Task PublishedStatePreservesSupportedExistingHostMappings(
        string sandboxPath,
        string relativePath
    )
    {
        // Arrange
        SetPublishedState((sandboxPath, relativePath));

        // Act
        var result = await LoadAsync();

        // Assert
        var state = result.Metadata.State.Should().ContainSingle().Which;
        state.SandboxPath.Should().Be(sandboxPath);
        state.RelativePath.Should().Be(relativePath);
        _calls
            .Should()
            .NotContain(call =>
                call.Arguments.Contains("build") || call.Arguments.Contains("create")
            );
    }

    private void SetPublishedState(params (string SandboxPath, string RelativePath)[] state)
    {
        _metadata["State"] = JsonSerializer.SerializeToNode(
            state.Select(entry => new { entry.SandboxPath, entry.RelativePath })
        );
        var descriptor = new
        {
            schemaVersion = "3",
            kind = "workload",
            kits = new[] { new { @ref = Config } },
            capabilities = state.Select(entry => new
            {
                type = "com.docker.sandbox/host-mount@1",
                config = new { path = entry.SandboxPath },
            }),
        };
        _rawManifests[PinnedSet] = JsonSerializer.Serialize(
            new
            {
                schemaVersion = 2,
                annotations = new Dictionary<string, string>
                {
                    ["vnd.docker.sandbox.kit.descriptor"] = JsonSerializer.Serialize(descriptor),
                },
            }
        );
    }
}
