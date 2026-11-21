using Abox;
using Abox.Cli;
using Abox.Configuration;
using Abox.Features;
using Abox.Https;
using Abox.Infrastructure;
using Abox.Naming;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

internal sealed class PortsCommandFixture : IDisposable
{
    public TempDirectory Root { get; } = new();
    public TestConsole Console { get; } = new();
    public ISbxClient Sbx { get; } = Substitute.For<ISbxClient>();
    public string Location { get; }
    public string Name => SandboxName.For(Location);
    private readonly CliTestApplication application;

    public PortsCommandFixture(
        AboxConfig? config = null,
        IProcessRunner? runner = null,
        HostDevelopmentCertificate? exporter = null
    )
    {
        Location = Root.CreateDirectory("folder");
        Sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 51, 0));
        Sbx.DiagnoseAsync(Arg.Any<CancellationToken>()).Returns(new DiagnoseReport([]));
        Sbx.FindSandboxAsync(Name, Arg.Any<CancellationToken>())
            .Returns(new SandboxInfo(Name, "running", null));
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists(Arg.Any<string>()).Returns(true);
        application = new CliTestApplication(
            Console,
            AboxPaths.For(Root.Path, Root.Path),
            new TrailingArguments()
        );
        application
            .Services.AddSingleton(config ?? new AboxConfig { DefaultLocation = Location })
            .AddSingleton<ISbxClient>(
                runner is null ? Sbx : new SbxClient(runner, AboxPaths.For(Root.Path, Root.Path))
            )
            .AddSingleton(programs);
        if (exporter is not null)
            application.Services.AddSingleton(exporter);
        _ = application.Provider;
    }

    public Task<int> Run(params string[] args) =>
        application.Provider.GetRequiredService<AboxRootCommand>().InvokeAsync(["ports", .. args]);

    public Task<int> Run(CancellationToken cancellationToken, params string[] args) =>
        application
            .Provider.GetRequiredService<AboxRootCommand>()
            .Parse(["ports", .. args])
            .InvokeAsync(cancellationToken: cancellationToken);

    public async Task NoPublishing() =>
        await Sbx.DidNotReceive()
            .EnsureApplicationPortsAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<ApplicationPortMapping>>(),
                Arg.Any<CancellationToken>()
            );

    public void Dispose()
    {
        application.Dispose();
        Root.Dispose();
    }
}
