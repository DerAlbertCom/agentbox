using System.Text.Json;
using Abox.Cli;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sbx;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace Abox.Tests;

/// <summary><c>abox run</c> through the real Run flow, Launchers and sbx adapter.</summary>
public sealed class RunCommandTests
{
    [Fact]
    public async Task TheShellRunsALoginShellInTheRuntimeWorkspace()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var exitCode = await fixture.Run("shell");

        // Assert
        exitCode.Should().Be(0, fixture.Console.AllText);
        var exec = fixture.Launch();
        exec.Arguments.Take(5).Should().Equal("exec", "-i", "-t", "-w", "/home/agent/workspace");
        exec.Arguments[5].Should().Be(fixture.Name);
        exec.Arguments.TakeLast(2).Should().Equal("bash", "-l");
    }

    [Fact]
    public async Task TheShellRunsTheArgvInTheHostFolderWhenTheRuntimeDoesNotReportTheWorkspace()
    {
        // Arrange
        using var fixture = new Fixture { Workspace = "" };

        // Act
        var exitCode = await fixture.Run("shell", "ls", "-la");

        // Assert
        exitCode.Should().Be(0, fixture.Console.AllText);
        var exec = fixture.Launch();
        exec.Arguments.Take(5).Should().Equal("exec", "-i", "-t", "-w", fixture.Location);
        exec.Arguments.TakeLast(2).Should().Equal("ls", "-la");
    }

    [Fact]
    public async Task AMissingHostProgramStopsBeforeTheSandboxIsLookedUp()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.MissingOnHost.Add("hc");

        // Act
        var exitCode = await fixture.Run("herdr-app");

        // Assert
        exitCode.Should().Be(1);
        fixture
            .Console.Errors.Should()
            .Equal(
                "The launcher 'herdr-app' needs 'hc' on the host.",
                "Install herdr-core: it comes with the Herdr desktop app."
            );
        fixture.Calls.Should().NotContain(call => call.Arguments[0] == "ls");
    }

    [Fact]
    public async Task AMissingSandboxProgramPrintsTheCreateHint()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.MissingInSandbox.Add("hc");
        fixture.Record(
            new SandboxRecord
            {
                Team = "other-team",
                Squad = "ac",
                Tag = "release",
                KitReference = "published-set",
            }
        );

        // Act
        var exitCode = await fixture.Run("herdr-app");

        // Assert
        exitCode.Should().Be(1);
        fixture
            .Console.Errors.Should()
            .Equal(
                $"The image of '{fixture.Name}' has no 'hc'. The image predates the 'herdr-app' launcher.",
                $"Create the sandbox again for this folder: abox create --team other-team --squad ac --tag release --location \"{fixture.Location}\""
            );
        fixture.Calls.Should().NotContain(call => call.FileName != "sbx");
    }

    [Fact]
    public async Task ANewerBuildIsOfferedBeforeTheLauncherStarts()
    {
        // Arrange
        using var fixture = new Fixture();
        fixture.Console.Answer = false;
        fixture.Record(
            new SandboxRecord
            {
                Team = "sample",
                Squad = "ac",
                Tag = "latest",
                BuildCreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            }
        );
        fixture
            .Provider.GetRequiredService<BuildStore>()
            .Save(
                new BuildManifest
                {
                    Squad = "ac",
                    Tag = "latest",
                    CreatedAt = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero),
                    Reference = ImageName.OfTeam("sample", "latest"),
                    LocalWorkload = LocalWorkloadFactory.Artifact(fixture.Root),
                }
            );

        // Act
        var exitCode = await fixture.Run("shell");

        // Assert
        exitCode.Should().Be(0, fixture.Console.AllText);
        fixture
            .Console.Output.Should()
            .Equal(
                "The squad 'ac' has a newer build, from 2026-02-01 00:00.",
                "Replace the sandbox with it?"
            );
        fixture.Calls.Should().NotContain(call => call.Arguments[0] == "rm");
        fixture.Launch().Arguments.TakeLast(2).Should().Equal("bash", "-l");
    }

    [Fact]
    public async Task HerdrOpensASpaceForTheSandboxWithTheArgv()
    {
        // Arrange
        using var fixture = new Fixture();

        // Act
        var exitCode = await fixture.Run("herdr-app", "tool", "a b");

        // Assert
        exitCode.Should().Be(0, fixture.Console.AllText);
        fixture
            .Calls.Single(call => call.Arguments.Contains("add"))
            .Arguments.Should()
            .Equal(
                "machine",
                "add",
                "--via",
                $"abox pipe {fixture.Name} -- sh -c",
                "--name",
                fixture.Name
            );
        fixture
            .Calls.Single(call => call.Arguments.Contains("space.new"))
            .Arguments.Should()
            .Equal(
                "do",
                "space.new",
                "--name",
                "folder",
                "--folder",
                "/home/agent/workspace",
                "--machine",
                fixture.Name,
                "--argv",
                JsonSerializer.Serialize(new[] { "tool", "a b" })
            );
    }

    [Fact]
    public async Task HerdrRefusesToOpenWhenLaunchReadinessFails()
    {
        // Arrange
        using var fixture = new Fixture { ExecExitCode = 78 };

        // Act
        var exitCode = await fixture.Run("herdr-app");

        // Assert
        exitCode.Should().Be(1);
        fixture
            .Console.Errors.Should()
            .ContainSingle()
            .Which.Should()
            .Contain(
                "Herdr was not opened: sandbox HTTPS launch readiness failed. Exit stale agents and explicitly initialize fresh core/service parents through abox after saving work; rerun 'abox ports' for recovery guidance. No core or app was restarted."
            );
        fixture.Calls.Should().NotContain(call => call.FileName != "sbx");
    }

    private sealed class Fixture : IDisposable
    {
        private readonly CliTestApplication application;

        public Fixture()
        {
            Location = Root.CreateDirectory("folder");
            var runner = Substitute.For<IProcessRunner>();
            runner
                .RunAsync(Arg.Any<ProcessRequest>(), Arg.Any<CancellationToken>())
                .Returns(call => Respond(call.Arg<ProcessRequest>()));
            var programs = Substitute.For<IHostPrograms>();
            programs
                .Exists(Arg.Any<string>())
                .Returns(call => !MissingOnHost.Contains(call.Arg<string>()));
            application = new CliTestApplication(
                Console,
                AboxPaths.For(Root.Path, Root.Path),
                new TrailingArguments()
            );
            application
                .Services.AddSingleton(new AboxConfig { DefaultTeam = "sample" })
                .AddSingleton(runner)
                .AddSingleton<ISbxClient, SbxClient>()
                .AddSingleton(programs);
        }

        public TempDirectory Root { get; } = new();
        public TestConsole Console { get; } = new();
        public string Location { get; }
        public string Name => SandboxName.For(Location);
        public ServiceProvider Provider => application.Provider;
        public string Workspace { get; init; } = "/home/agent/workspace";
        public int ExecExitCode { get; init; }
        public HashSet<string> MissingOnHost { get; } = [];
        public HashSet<string> MissingInSandbox { get; } = [];
        public List<ProcessRequest> Calls { get; } = [];

        public void Record(SandboxRecord record) =>
            Provider.GetRequiredService<SandboxRecordStore>().Save(Name, record);

        public Task<int> Run(string launcher, params string[] argv)
        {
            Provider.GetRequiredService<TrailingArguments>().Values = argv;
            return Provider
                .GetRequiredService<AboxRootCommand>()
                .InvokeAsync(["run", launcher, "--location", Location]);
        }

        public ProcessRequest Launch() =>
            Calls.Single(call =>
                call.FileName == "sbx" && call.Arguments[0] == "exec" && call.AttachConsole
            );

        private ProcessResult Respond(ProcessRequest request)
        {
            Calls.Add(request);
            if (request.FileName == "hc")
                return new ProcessResult(0, """{"machines":[]}""", "");
            if (request.FileName == "herdr-app")
                return new ProcessResult(0, """{"errors":[]}""", "");
            return request.Arguments[0] switch
            {
                "version" => new(0, """{"client":{"version":"0.51.0"}}""", ""),
                "diagnose" => new(0, """{"checks":[]}""", ""),
                "ls" => new(0, $$"""[{"name":"{{Name}}","status":"running"}]""", ""),
                "exec" when request.Arguments.Contains("command -v \"$1\"") => new(
                    MissingInSandbox.Contains(request.Arguments[^1]) ? 1 : 0,
                    "",
                    ""
                ),
                "exec" when request.Arguments.Contains("printf %s \"$WORKSPACE_DIR\"") => new(
                    0,
                    Workspace,
                    ""
                ),
                "exec" => new(ExecExitCode, "", ""),
                _ => new(0, "", ""),
            };
        }

        public void Dispose()
        {
            application.Dispose();
            Root.Dispose();
        }
    }
}
