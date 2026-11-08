using Abox.Build;
using Abox.Configuration;
using Abox.Features.Create;
using Abox.Features.Prune;
using Abox.Features.Run;
using Abox.Features.Secrets;
using Abox.Kits;
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
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(new SbxOutcome(0, string.Empty));
        _sbx.RemoveSecretAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
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

        var exitCode = await _service.ListAsync(Request(), CancellationToken.None);

        exitCode.Should().Be(0);
        _console.AllText.Should().Contain("github");
        _console.AllText.Should().Contain("pi-config");
        _console.AllText.Should().Contain("not set");
    }

    [Fact]
    public async Task AStoredSecretIsShownAsStored()
    {
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

        await _service.ListAsync(Request(), CancellationToken.None);

        _console.AllText.Should().Contain("stored");
    }

    [Fact]
    public async Task AValueIsStoredUnderItsService()
    {
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

        var exitCode = await _service.SetAsync(Request(), CancellationToken.None);

        exitCode.Should().Be(0);
        await _sbx.Received(1).SetSecretAsync("github", "the-token", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task AValueIsEncodedWhenTheConfigurationSaysSo()
    {
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

        await _service.SetAsync(Request(), CancellationToken.None);

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

        await _service.SetAsync(Request(), CancellationToken.None);

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

        await _service.SetAsync(Request(), CancellationToken.None);

        await _sbx.Received(1)
            .SetCustomSecretAsync(
                "elastic",
                "api.elastic.co",
                "ELASTIC_API_KEY",
                "the-key",
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task AServiceThatIsNotDeclaredIsAnError()
    {
        WriteKitWithService("github");
        _root.File(
            "teams/sample/ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            """
        );

        var exitCode = await _service.SetAsync(Request(), CancellationToken.None);

        exitCode.Should().Be(1);
        await _sbx.DidNotReceive()
            .SetSecretAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .SetCustomSecretAsync(
                Arg.Any<string>(),
                Arg.Any<string?>(),
                Arg.Any<string?>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task OnlyTheGivenServiceIsSet()
    {
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

        await _service.SetAsync(
            new SecretsRequest("ac", _root.Path, ["github"]),
            CancellationToken.None
        );

        await _sbx.Received(1).SetSecretAsync("github", "the-token", Arg.Any<CancellationToken>());
        await _sbx.DidNotReceive()
            .SetSecretAsync("elastic", Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASecretIsRemoved()
    {
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

        var exitCode = await _service.RemoveAsync(Request(), CancellationToken.None);

        exitCode.Should().Be(0);
        await _sbx.Received(1).RemoveSecretAsync("github", null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ASecretOfAServiceSbxDoesNotKnowIsRemovedByItsHost()
    {
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

        await _service.RemoveAsync(Request(), CancellationToken.None);

        await _sbx.Received(1)
            .RemoveSecretAsync("elastic", "api.elastic.co", Arg.Any<CancellationToken>());
    }

    private SecretsRequest Request() => new("ac", _root.Path, []);

    private void WriteKitWithService(string service)
    {
        _root.Directory_("kits/pi-config");
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
