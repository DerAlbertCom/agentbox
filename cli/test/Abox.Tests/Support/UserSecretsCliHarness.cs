using Abox.Cli;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;
using Abox.UserSecrets;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

/// <summary>Disposable synthetic host stores and the real CLI with fake external tools.</summary>
internal sealed class UserSecretsCliHarness : IDisposable
{
    public TempDirectory Root { get; } = new();
    public TestConsole Console { get; } = new();
    public ISbxClient Sbx { get; } = Substitute.For<ISbxClient>();
    public string Location { get; }
    public string Name => SandboxName.For(Location);
    private readonly CliTestApplication application;
    public IServiceCollection Services => application.Services;
    public ServiceProvider Provider => application.Provider;

    public UserSecretsCliHarness(
        AboxConfig? config = null,
        bool unix = false,
        string? configYaml = null,
        bool existingSandbox = true
    )
    {
        Location = Root.CreateDirectory("folder");
        Sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 51, 0));
        Sbx.DiagnoseAsync(Arg.Any<CancellationToken>()).Returns(new DiagnoseReport([]));
        if (existingSandbox)
            ExistingSandbox();
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists(Arg.Any<string>()).Returns(true);
        application = new CliTestApplication(
            Console,
            AboxPaths.For(Root.Path, Root.Path),
            new TrailingArguments()
        );
        if (configYaml is null)
            Services.AddSingleton(config ?? new AboxConfig { DefaultLocation = Location });
        else
            Root.File("config.yaml", configYaml);
        Services.AddSingleton(
            new UserSecretsHost(
                unix ? null : Root.CreateDirectory("appdata"),
                Root.CreateDirectory("home")
            )
        );
        Services.AddSingleton(Sbx);
        Services.AddSingleton(programs);
    }

    public void ExistingSandbox(DateTimeOffset? createdAt = null) =>
        Sbx.ListSandboxesAsync(Arg.Any<CancellationToken>())
            .Returns(new[] { new SandboxInfo(Name, "running", createdAt) });

    public void Configure(string json) => Root.File("folder/abox.json", json);

    public string Store(string id, string json) =>
        Root.File($"appdata/Microsoft/UserSecrets/{id}/secrets.json", json);

    public Task<int> Invoke(params string[] args) =>
        Provider.GetRequiredService<AboxRootCommand>().InvokeAsync(args);

    public Task<int> Run(params string[] args) =>
        Invoke(["user-secrets", "--location", Location, .. args]);

    public Task<int> RunDefault() => Invoke("user-secrets");

    public async Task NoWrites() =>
        await Sbx.DidNotReceive()
            .WriteUserSecretsAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            );

    public string Written(string id) => WrittenStores().Single(write => write.Id == id).Json;

    public string LatestWritten(string? id = null) =>
        WrittenStores().Last(write => id is null || write.Id == id).Json;

    private IEnumerable<(string Id, string Json)> WrittenStores() =>
        Sbx.ReceivedCalls()
            .Where(call => call.GetMethodInfo().Name == nameof(ISbxClient.WriteUserSecretsAsync))
            .Select(call => ((string)call.GetArguments()[1]!, (string)call.GetArguments()[2]!));

    public void Dispose()
    {
        application.Dispose();
        Root.Dispose();
    }
}
