using Abox.Kits;
using Abox.Naming;
using Abox.Sandboxes;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SandboxCreatorCredentialsTests
{
    private readonly SandboxCreatorFixture _fixture = new();

    public SandboxCreatorCredentialsTests()
    {
        _fixture.SaveBuild();
        _fixture
            .Sbx.ListSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new HostSecrets([], []));
        _fixture
            .Sbx.SetSecretAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
        _fixture.Root.File(
            "teams/sample/ac.abox",
            "workload: mti-ca-dotnet\nkits:\n  - github-auth\nsecrets:\n  - service: github\n"
        );
        _fixture.Root.File(
            "teams/sample/mti-ca-dotnet.workload",
            "template: docker.io/sbx/shell-docker-image\n"
        );
        _fixture.Root.File(
            "kits/github-auth/github-auth.yaml",
            """
            schemaVersion: "3"
            kind: mixin
            capabilities:
              - type: com.docker.sandbox/credential@1
                optional: true
                config:
                  service: github
                  apiKey:
                    name: GITHUB_TOKEN
                    inject:
                      - domain: api.github.com
            """
        );
    }

    [Fact]
    public async Task MissingCredentialsAreAskedForBeforeTheExistingSandboxIsRemoved()
    {
        // Arrange
        var name = SandboxName.For(_fixture.Root.Path);
        _fixture
            .Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        _fixture.Console.Answers.Add("the-token");

        // Act
        var exitCode = await _fixture.Service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        Received.InOrder(() =>
        {
            _fixture.Sbx.SetSecretAsync("github", "the-token", Arg.Any<CancellationToken>());
            _fixture.Sbx.RemoveSandboxAsync(name, Arg.Any<CancellationToken>());
            _fixture.Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>());
        });
        new SbxCredentialBindings(TestServiceGraphs.BindingsFile(_fixture.Paths))
            .Covers("github", ["api.github.com"])
            .Should()
            .BeTrue();
    }

    [Fact]
    public async Task ARefusedReplacementAsksForNoCredentials()
    {
        // Arrange
        var name = SandboxName.For(_fixture.Root.Path);
        _fixture
            .Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(name, "running", DateTimeOffset.Now) });
        _fixture.Console.Answer = false;

        // Act
        var action = () => _fixture.Service.CreateAsync(Request(), CancellationToken.None);

        // Assert
        await action.Should().ThrowAsync<AboxException>();
        await _fixture.Sbx.DidNotReceive().ListSecretsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TheNotesOfSbxAreShownAfterTheSandboxIsCreated()
    {
        // Arrange
        _fixture
            .Sbx.CreateAsync(Arg.Any<SbxCreateRequest>(), Arg.Any<CancellationToken>())
            .Returns(
                new SbxOutcome(
                    0,
                    "Creating sandbox...\n"
                        + "Note: no binding authorizes elastic — the credential was not injected.\n"
                        + "Sandbox created\n"
                )
            );

        // Act
        var exitCode = await _fixture.Service.CreateAsync(
            _fixture.Request(),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        _fixture
            .Console.Output.Should()
            .Contain("Note: no binding authorizes elastic — the credential was not injected.");
        _fixture.Console.Output.Should().NotContain("Creating sandbox...");
    }

    private SandboxCreationRequest Request() =>
        _fixture.Request() with
        {
            Team = new SquadResolver.ResolvedTeam(
                "sample",
                Path.Combine(_fixture.Root.Path, "teams", "sample"),
                ["ac"]
            )
            {
                RootDirectory = _fixture.Root.Path,
            },
        };
}
