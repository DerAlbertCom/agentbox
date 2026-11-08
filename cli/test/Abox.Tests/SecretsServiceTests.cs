using Abox.Build;
using Abox.Configuration;
using Abox.Kits;
using Abox.Services;

namespace Abox.Tests;

public sealed class SecretsServiceTests
{
    private readonly TempDirectory _root = new();
    private readonly TestConsole _console = new();
    private readonly FakeSbxClient _sbx = new();
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

        _root.File("ac.abox", "workload: mti-ca-dotnet\n");
        _root.File("kits/mti-ca-dotnet.workload", "template: docker.io/sbx/shell-docker-image\n");
    }

    [Fact]
    public async Task TheServicesOfTheKitsAreListed()
    {
        WriteKitWithService("github");
        _root.File(
            "ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            secrets:
              - service: github
            """
        );

        var exitCode = await _service.ListAsync(Request(), CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Contains("github", _console.AllText);
        Assert.Contains("pi-config", _console.AllText);
        Assert.Contains("not set", _console.AllText);
    }

    [Fact]
    public async Task AStoredSecretIsShownAsStored()
    {
        WriteKitWithService("github");
        _root.File(
            "ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            secrets:
              - service: github
            """
        );
        _sbx.Secrets.Add("github");

        await _service.ListAsync(Request(), CancellationToken.None);

        Assert.Contains("stored", _console.AllText);
    }

    [Fact]
    public async Task AValueIsStoredUnderItsService()
    {
        WriteKitWithService("github");
        _root.File(
            "ac.abox",
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

        Assert.Equal(0, exitCode);
        Assert.Equal(("github", "the-token"), Assert.Single(_sbx.SetSecrets));
    }

    [Fact]
    public async Task AValueIsEncodedWhenTheConfigurationSaysSo()
    {
        WriteKitWithService("github");
        _root.File(
            "ac.abox",
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

        Assert.Equal(
            Convert.ToBase64String("the-token"u8.ToArray()),
            Assert.Single(_sbx.SetSecrets).Value
        );
    }

    [Fact]
    public async Task AValueIsBuiltFromItsInputs()
    {
        WriteKitWithService("registry");
        _root.File(
            "ac.abox",
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

        Assert.Equal(
            Convert.ToBase64String("albert:secret"u8.ToArray()),
            Assert.Single(_sbx.SetSecrets).Value
        );
    }

    [Fact]
    public async Task ASecretOfAServiceSbxDoesNotKnowIsStoredWithItsHost()
    {
        WriteKitWithService("elastic");
        _root.File(
            "ac.abox",
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

        var custom = Assert.Single(_sbx.CustomSecrets);
        Assert.Equal("elastic", custom.Service);
        Assert.Equal("api.elastic.co", custom.Host);
        Assert.Equal("ELASTIC_API_KEY", custom.EnvironmentVariable);
        Assert.Equal("the-key", custom.Value);
    }

    [Fact]
    public async Task AServiceThatIsNotDeclaredIsAnError()
    {
        WriteKitWithService("github");
        _root.File(
            "ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            """
        );

        var exitCode = await _service.SetAsync(Request(), CancellationToken.None);

        Assert.Equal(1, exitCode);
        Assert.Empty(_sbx.SetSecrets);
    }

    [Fact]
    public async Task OnlyTheGivenServiceIsSet()
    {
        WriteKitWithService("github");
        _root.File(
            "ac.abox",
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

        Assert.Equal("github", Assert.Single(_sbx.SetSecrets).Service);
    }

    [Fact]
    public async Task ASecretIsRemoved()
    {
        WriteKitWithService("github");
        _root.File(
            "ac.abox",
            """
            workload: mti-ca-dotnet
            kits:
              - pi-config
            secrets:
              - service: github
            """
        );
        _sbx.Secrets.Add("github");

        var exitCode = await _service.RemoveAsync(Request(), CancellationToken.None);

        Assert.Equal(0, exitCode);
        Assert.Equal(["github"], _sbx.RemovedSecrets);
    }

    [Fact]
    public async Task ASecretOfAServiceSbxDoesNotKnowIsRemovedByItsHost()
    {
        WriteKitWithService("elastic");
        _root.File(
            "ac.abox",
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

        Assert.Equal(["elastic"], _sbx.RemovedSecrets);
        Assert.DoesNotContain("api.elastic.co", _sbx.CustomTargets);
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
