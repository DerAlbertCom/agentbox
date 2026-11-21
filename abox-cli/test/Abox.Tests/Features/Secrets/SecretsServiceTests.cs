using Abox.Configuration;
using Abox.Features.Prune;
using Abox.Features.Run;
using Abox.Features.Secrets;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class SecretsServiceTests
{
    private readonly TempDirectory _root = new();
    private readonly TestConsole _console = new();
    private readonly ISbxClient _sbx = Substitute.For<ISbxClient>();
    private readonly SecretsService _service;
    private readonly SquadResolver _resolver = new(new AboxConfig());

    public SecretsServiceTests()
    {
        _service = new SecretsService(
            _resolver,
            new PersonalConfigurations(
                AboxPaths.For(Path.Combine(_root.Path, "config"), _root.Path)
            ),
            _sbx,
            _console
        );

        _sbx.ListSecretsAsync(Arg.Any<CancellationToken>()).Returns(new HostSecrets([], []));
        _sbx.SetSecretAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));
        _sbx.SetCustomSecretAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new SbxOutcome(0, string.Empty));
        _sbx.RemoveSecretAsync(Arg.Any<SecretDeclaration>(), Arg.Any<CancellationToken>())
            .Returns(new SbxOutcome(0, string.Empty));

        _root.File("teams/sample/ac.abox", "workload: mti-ca-dotnet\n");
        _root.File(
            "teams/sample/mti-ca-dotnet.workload",
            "template: docker.io/sbx/shell-docker-image\n"
        );
    }

    [Fact]
    public async Task TheServicesOfTheKitsAreListed()
    {
        // Arrange
        WriteKitWithService("github");
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            secrets:
              - service: github
            """
        );

        // Act
        var exitCode = await _service.ListAsync(Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        _console.AllText.Should().Contain("github");
        _console.AllText.Should().Contain("pi-config");
        _console.AllText.Should().Contain("not set");
    }

    [Theory]
    [InlineData("list")]
    [InlineData("set")]
    [InlineData("remove")]
    public async Task SecretsCommandsUseTheConfiguredDefaultTeam(string action)
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
            "workload: mti-ca-dotnet\nsecrets: [{service: github}]\n"
        );
        _console.Answers.Add("the-token");
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists("sbx").Returns(true);
        _sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 51, 0));
        _sbx.DiagnoseAsync(Arg.Any<CancellationToken>()).Returns(new DiagnoseReport([]));
        var command = new SecretsCommand(
            new Lazy<AboxConfig>(() => new AboxConfig { DefaultTeam = "sample" }),
            new Preflight(_console, _sbx, programs),
            new Lazy<SecretsService>(() => _service),
            _console
        );

        // Act
        var exitCode = await command
            .Parse([action, "--squad", "ac", "--root", _root.Path])
            .InvokeAsync();

        // Assert
        exitCode.Should().Be(0);
        _console.AllText.Should().NotContain("No team is given");
    }

    [Fact]
    public async Task AStoredSecretIsShownAsStored()
    {
        // Arrange
        WriteKitWithService("github");
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            secrets:
              - service: github
            """
        );
        _sbx.ListSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new HostSecrets(["github"], []));

        // Act
        await _service.ListAsync(Request(), CancellationToken.None);

        // Assert
        _console.AllText.Should().Contain("stored");
    }

    [Fact]
    public async Task AValueIsStoredUnderItsService()
    {
        // Arrange
        WriteKitWithService("github");
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            secrets:
              - service: github
            """
        );
        _console.Answers.Add("the-token");

        // Act
        var exitCode = await _service.SetAsync(Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        await _sbx.Received(1).SetSecretAsync("github", "the-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AValueIsEncodedWhenTheConfigurationSaysSo()
    {
        // Arrange
        WriteKitWithService("github");
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            secrets:
              - service: github
                encoding: base64
            """
        );
        _console.Answers.Add("the-token");

        // Act
        await _service.SetAsync(Request(), CancellationToken.None);

        // Assert
        await _sbx.Received(1)
            .SetSecretAsync(
                "github",
                Convert.ToBase64String("the-token"u8.ToArray()),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AValueIsBuiltFromItsInputs()
    {
        // Arrange
        WriteKitWithService("registry");
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            secrets:
              - service: registry
                inputs:
                  - name: username
                  - name: password
                encoding: base64
            """
        );
        _console.Answers.Add("albert");
        _console.Answers.Add("secret");

        // Act
        await _service.SetAsync(Request(), CancellationToken.None);

        // Assert
        await _sbx.Received(1)
            .SetSecretAsync(
                "registry",
                Convert.ToBase64String("albert:secret"u8.ToArray()),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ASecretOfAServiceSbxDoesNotKnowIsStoredWithItsHost()
    {
        // Arrange
        WriteKitWithService("elastic");
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            secrets:
              - service: elastic
                host: api.elastic.co
                env: ELASTIC_API_KEY
            """
        );
        _console.Answers.Add("the-key");

        // Act
        await _service.SetAsync(Request(), CancellationToken.None);

        // Assert
        await _sbx.Received(1)
            .SetCustomSecretAsync(
                "api.elastic.co",
                "ELASTIC_API_KEY",
                "the-key",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AServiceThatIsNotDeclaredIsAnError()
    {
        // Arrange
        WriteKitWithService("github");
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            """
        );

        // Act
        var exitCode = await _service.SetAsync(Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(1);
        await _sbx.DidNotReceive()
            .SetSecretAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .SetCustomSecretAsync(
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task OnlyTheGivenServiceIsSet()
    {
        // Arrange
        WriteKitWithService("github");
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            secrets:
              - service: github
              - service: elastic
            """
        );
        _console.Answers.Add("the-token");

        // Act
        await _service.SetAsync(
            new SecretsRequest("sample", "ac", _root.Path, ["github"]),
            CancellationToken.None
        );

        // Assert
        await _sbx.Received(1).SetSecretAsync("github", "the-token", Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .SetSecretAsync("elastic", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASecretIsRemoved()
    {
        // Arrange
        WriteKitWithService("github");
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            secrets:
              - service: github
            """
        );

        // Act
        var exitCode = await _service.RemoveAsync(Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        await _sbx.Received(1)
            .RemoveSecretAsync(
                Arg.Is<SecretDeclaration>(secret => secret.Service == "github" && !secret.IsCustom),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task ASecretOfAServiceSbxDoesNotKnowIsRemovedByItsHost()
    {
        // Arrange
        WriteKitWithService("elastic");
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            secrets:
              - service: elastic
                host: api.elastic.co
                env: ELASTIC_API_KEY
            """
        );
        var (service, runner) = CreateRuntimeService();

        // Act
        var exitCode = await service.RemoveAsync(Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        await runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.SequenceEqual(
                        new[] { "secret", "rm", "--host", "api.elastic.co", "--force" }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task EnvironmentOnlyCustomSecretsAreRemovedByTheirEnvironmentVariable()
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
            "workload: mti-ca-dotnet\nsecrets: [{service: elastic, env: ELASTIC_API_KEY}]\n"
        );
        _console.Answers.Add("the-key");
        var (service, runner) = CreateRuntimeService();

        // Act
        var setExitCode = await service.SetAsync(Request(), CancellationToken.None);

        // Assert
        setExitCode.Should().Be(0);

        // Act
        var removeExitCode = await service.RemoveAsync(Request(), CancellationToken.None);

        // Assert
        removeExitCode.Should().Be(0);

        await runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.SequenceEqual(
                        new[]
                        {
                            "secret",
                            "set-custom",
                            "--env",
                            "ELASTIC_API_KEY",
                            "--value-stdin",
                        }
                    )
                    && request.StandardInput == "the-key"
                ),
                Arg.Any<CancellationToken>()
            );
        await runner
            .Received(1)
            .RunAsync(
                Arg.Is<ProcessRequest>(request =>
                    request.Arguments.SequenceEqual(
                        new[] { "secret", "rm", "--env-name", "ELASTIC_API_KEY", "--force" }
                    )
                ),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task EnvironmentOnlyCustomSecretsAreRecognisedAsStored()
    {
        // Arrange
        _root.File(
            "teams/sample/ac.abox",
            "workload: mti-ca-dotnet\nsecrets: [{service: elastic, env: ELASTIC_API_KEY}]\n"
        );
        _sbx.ListSecretsAsync(Arg.Any<CancellationToken>())
            .Returns(new HostSecrets([], [new CustomSecret([], "ELASTIC_API_KEY")]));

        // Act
        var exitCode = await _service.ListAsync(Request(), CancellationToken.None);

        // Assert
        exitCode.Should().Be(0);
        _console.AllText.Should().Contain("stored").And.NotContain("not set");
    }

    private (SecretsService Service, IProcessRunner Runner) CreateRuntimeService()
    {
        var runner = Substitute.For<IProcessRunner>();
        runner
            .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ProcessResult(0, "", ""));
        var paths = AboxPaths.For(Path.Combine(_root.Path, "config"), _root.Path);
        var service = new SecretsService(
            _resolver,
            new PersonalConfigurations(paths),
            new SbxClient(runner, paths),
            _console
        );
        return (service, runner);
    }

    private SecretsRequest Request() => new("sample", "ac", _root.Path, []);

    private void WriteKitWithService(string service)
    {
        _root.CreateDirectory("kits/pi-config");
        _root.File(
            "kits/pi-config/pi-config.yaml",
            $"""
            schemaVersion: "3"
            kind: mixin
            capabilities:
              - type: com.docker.sandbox/credential@1
                config:
                  service: {service}
                  phase: runtime
            """
        );
    }
}
