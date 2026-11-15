using System.Text.RegularExpressions;
using Abox.Configuration;
using Abox.Features.Build;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class AgentGuidanceBuildTests
{
    [Theory(SkipExceptions = [typeof(ShellUnavailableException)])]
    [InlineData("opencode-config", "/home/agent/.config/opencode", "~/.config/opencode/state")]
    [InlineData("pi-config", "/home/agent/.pi/agent", "mise upgrade --bump")]
    public async Task BuiltGuidanceEntersTheRuntimeWorkspaceInsteadOfTheImageWorkingDirectory(
        string kitName,
        string agentDirectory,
        string agentConvention
    )
    {
        // Arrange
        using var root = new TempDirectory();
        var source = KitDescriptor.Load(RepositoryKit(kitName))!;
        var kitDirectory = root.Directory_("kits/" + kitName);
        File.Copy(source.DescriptorPath, Path.Combine(kitDirectory, kitName + ".yaml"));
        root.File("teams/sample/ac.abox", $"workload: sample\nkits: [{kitName}]\n");
        root.File("teams/sample/sample.workload", "template: example.com/base:1\n");
        root.File("runtime workspace/project.txt", "the runtime-mounted project\n");
        root.File("image-workdir/project.txt", "the image default is not the project\n");
        var paths = AboxPaths.For(root.Directory_("config"), root.Directory_("state"));
        var store = new BuildStore(paths);
        var resolver = new SquadResolver(new AboxConfig());
        var runner = Substitute.For<IProcessRunner>();
        var sbx = Substitute.For<ISbxClient>();
        var service = new BuildService(
            resolver,
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            new PersonalConfigurations(paths),
            store,
            LocalWorkloadTest.Images(runner),
            sbx,
            new TestConsole()
        );

        // Act
        var exitCode = await service.BuildAsync(
            new BuildRequest(
                [resolver.ResolveTeam(root.Path, "sample")],
                root.Path,
                "latest",
                IncludePersonalConfigurations: false,
                MetadataOnly: true
            ),
            CancellationToken.None
        );
        var manifest = store.Load("ac", "latest")!;
        var snapshot = BuildSources.Snapshot(manifest, root.Directory_("publication-snapshot"));
        var kit = manifest.Kits.Should().ContainSingle().Which;
        var descriptor = KitDescriptor.Load(snapshot[kit.Reference])!;
        var yaml = YamlReader.LoadFile(descriptor.DescriptorPath);
        var context = yaml.GetEntries("capabilities")
            .Should()
            .ContainSingle(entry => entry.GetString("type") == "com.docker.sandbox/agent-context@2")
            .Which.GetEntry("config")!;
        var guidance = context.GetString("content")!;
        var workspaceStep = Regex.Match(guidance, @"(?s)```sh\s*\n(.*?)\n```");

        // Assert
        exitCode.Should().Be(0);
        yaml.GetString("schemaVersion").Should().Be("3");
        yaml.GetString("kind").Should().Be("mixin");
        context.GetString("directory").Should().Be(agentDirectory);
        context.GetString("filename").Should().Be("AGENTS.md");
        guidance.Should().Contain(agentConvention).And.NotContain("/home/agent/workspace");
        workspaceStep.Success.Should().BeTrue("the guidance must show how to enter the workspace");

        // Act: follow the shipped workspace step from a different image working directory.
        var result = await new ProcessRunner().RunAsync(
            new ProcessRequest(
                Shell(),
                [
                    "-eu",
                    "-c",
                    "WORKSPACE_DIR=\"$PWD/runtime workspace\"; export WORKSPACE_DIR\n"
                        + "cd image-workdir\n"
                        + workspaceStep.Groups[1].Value
                        + "\nIFS= read -r project < project.txt\nprintf '%s\\n' \"$project\"",
                ],
                WorkingDirectory: root.Path
            ),
            CancellationToken.None
        );

        // Assert
        result.ExitCode.Should().Be(0, result.StandardError);
        result.StandardOutput.Should().Be("the runtime-mounted project" + Environment.NewLine);
        runner.ReceivedCalls().Should().BeEmpty();
        sbx.ReceivedCalls().Should().BeEmpty();
    }

    private static string RepositoryKit(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null
            && !File.Exists(Path.Combine(directory.FullName, "kits", name, name + ".yaml"))
        )
        {
            directory = directory.Parent;
        }
        return Path.Combine(directory!.FullName, "kits", name);
    }

    private static string Shell() =>
        OperatingSystem.IsWindows()
            ? (Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator)
                .Where(directory => File.Exists(Path.Combine(directory, "git.exe")))
                .Select(directory =>
                    Path.GetFullPath(Path.Combine(directory, "..", "usr", "bin", "sh.exe"))
                )
                .FirstOrDefault(File.Exists)
                ?? throw new ShellUnavailableException(
                    "Git for Windows sh is needed to execute the generated agent guidance."
                )
            : "sh";

    private sealed class ShellUnavailableException(string message) : Exception(message);
}
