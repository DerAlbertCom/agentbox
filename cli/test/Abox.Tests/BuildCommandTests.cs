using Abox.Build;
using Abox.Configuration;
using Abox.Features.Build;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuildCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MetadataOnlyPreparesRemoteMetadataWithoutAnyExternalPrograms(bool noVerify)
    {
        using var root = new TempDirectory();
        root.File(
            "teams/sample/ac.abox",
            "workload: sample\nstate: [~/.probe-state]\nmcp: [elastic]\n"
        );
        root.File("teams/sample/sample.workload", "template: example.com/base:1\n");
        var console = new TestConsole();
        var programs = Substitute.For<IHostPrograms>();
        var runner = Substitute.For<IProcessRunner>();
        var sbx = Substitute.For<ISbxClient>();
        var config = new AboxConfig();
        var paths = AboxPaths.For(
            Path.Combine(root.Path, "config"),
            Path.Combine(root.Path, "state")
        );
        var store = new BuildStore(paths);
        var resolver = new SquadResolver(config);
        var service = new BuildService(
            resolver,
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            new PersonalConfigurations(paths),
            store,
            new LocalWorkloadImages(programs, runner, console),
            sbx,
            console
        );
        var command = new BuildCommand(
            config,
            new Preflight(console, sbx, programs),
            resolver,
            service,
            console
        );
        var arguments = new List<string>
        {
            "--team",
            "sample",
            "--root",
            root.Path,
            "--tag",
            "remote",
            "--metadata-only",
            "--no-personal",
        };
        if (noVerify)
        {
            arguments.Add("--no-verify");
        }

        var exitCode = await command.Parse(arguments.ToArray()).InvokeAsync();

        exitCode.Should().Be(0);
        var manifest = store.Load("ac", "remote")!;
        manifest.LocalWorkload.Should().BeNull();
        manifest.Reference.Should().Be(ImageName.OfTeam("sample", "remote"));
        manifest.State.Should().ContainSingle();
        manifest.Mcp.Should().ContainSingle().Which.Name.Should().Be("elastic");
        manifest.SourceHashes.Should().NotBeEmpty();
        File.Exists(Path.Combine(manifest.WorkloadKitDirectory, "workload.yaml")).Should().BeTrue();
        console.AllText.Should().Contain("metadata only").And.NotContain("--tag remote --local");
        programs.ReceivedCalls().Should().BeEmpty();
        runner.ReceivedCalls().Should().BeEmpty();
        sbx.ReceivedCalls().Should().BeEmpty();
    }
}
