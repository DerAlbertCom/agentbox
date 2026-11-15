using System.Text.Json;
using Abox.Configuration;
using Abox.Features.Build;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuildTagCommandTests
{
    [Theory]
    [MemberData(nameof(ValidTags))]
    public async Task ValidTagsBuildAndRemainSelectableWithoutChangingTheirSpelling(
        bool metadataOnly,
        bool configuredTag,
        string tag
    )
    {
        // Arrange
        using var root = new TempDirectory();
        root.File("teams/sample/ac.abox", "workload: sample\n");
        root.File("teams/sample/sample.workload", "template: example.com/base:1\n");
        root.File(
            "config/config.yaml",
            "defaultTag: "
                + JsonSerializer.Serialize(configuredTag ? tag : "../ignored-invalid-default")
                + "\n"
        );
        var paths = AboxPaths.For(
            Path.Combine(root.Path, "config"),
            Path.Combine(root.Path, "state")
        );
        var console = new TestConsole();
        var programs = Substitute.For<IHostPrograms>();
        programs.Exists("sbx").Returns(true);
        programs.Exists("docker").Returns(true);
        var runner = LocalWorkloadTest.Runner();
        var sbx = Substitute.For<ISbxClient>();
        sbx.GetVersionAsync(Arg.Any<CancellationToken>()).Returns(new Version(0, 51, 0));
        sbx.DiagnoseAsync(Arg.Any<CancellationToken>()).Returns(new DiagnoseReport([]));
        var resolver = new SquadResolver(new AboxConfig());
        var store = new BuildStore(paths);
        var service = new BuildService(
            resolver,
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            new PersonalConfigurations(paths),
            store,
            new LocalWorkloadImages(programs, new OciRegistry(runner), console),
            sbx,
            console
        );
        var command = new BuildCommand(
            new Lazy<AboxConfig>(() => AboxConfigLoader.Load(paths)),
            new Preflight(console, sbx, programs),
            new Lazy<SquadResolver>(() => resolver),
            new Lazy<BuildService>(() => service),
            console
        );
        var arguments = new List<string> { "--team", "sample", "--root", root.Path };
        if (!configuredTag)
            arguments.AddRange(["--tag", tag]);
        if (metadataOnly)
            arguments.Add("--metadata-only");

        // Act
        var exitCode = await command.Parse(arguments.ToArray()).InvokeAsync();

        // Assert
        exitCode.Should().Be(0, console.AllText);
        var manifest = store.Load("ac", tag)!;
        manifest.Tag.Should().Be(tag);
        File.ReadAllText(Path.Combine(manifest.WorkloadKitDirectory, "workload.yaml"))
            .Should()
            .Contain("schemaVersion");
        (manifest.LocalWorkload is null).Should().Be(metadataOnly);
    }

    [Theory]
    [MemberData(nameof(InvalidTags))]
    public async Task UnsafeTagsFailBeforePreflightAndGeneratedAssetCleanup(
        bool metadataOnly,
        bool configuredTag,
        string input
    )
    {
        // Arrange
        using var root = new TempDirectory();
        using var outside = new TempDirectory();
        root.File("teams/sample/ac.abox", "workload: sample\n");
        root.File("teams/sample/sample.workload", "template: example.com/base:1\n");
        var script = outside.File("workload/scripts/keep.sh", "external script");
        var certificate = outside.File("workload/certificates/keep.pem", "external certificate");
        var paths = AboxPaths.For(
            Path.Combine(root.Path, "config"),
            Path.Combine(root.Path, "state")
        );
        var tag = input switch
        {
            "traversal" => Path.GetRelativePath(
                Path.Combine(paths.BuildsDirectory, "ac"),
                outside.Path
            ),
            "absolute" => outside.Path,
            "unvalued" => "",
            "overlong" => new string('a', 129),
            _ => input,
        };
        if (configuredTag)
        {
            root.File(
                "config/config.yaml",
                "defaultTag: " + (input == "unvalued" ? "" : JsonSerializer.Serialize(tag)) + "\n"
            );
        }
        var programs = Substitute.For<IHostPrograms>();
        var runner = Substitute.For<IProcessRunner>();
        var sbx = Substitute.For<ISbxClient>();
        var console = new TestConsole();
        var config = new AboxConfig();
        var resolver = new SquadResolver(config);
        var build = new BuildService(
            resolver,
            new WorkloadKitGenerator(),
            new StateMixinGenerator(),
            new PersonalConfigurations(paths),
            new BuildStore(paths),
            new LocalWorkloadImages(programs, new OciRegistry(runner), console),
            sbx,
            console
        );
        var command = new BuildCommand(
            new Lazy<AboxConfig>(() => AboxConfigLoader.Load(paths)),
            new Preflight(console, sbx, programs),
            new Lazy<SquadResolver>(() => resolver),
            new Lazy<BuildService>(() => build),
            console
        );
        var arguments = new List<string> { "--team", "sample", "--root", root.Path };
        if (!configuredTag)
        {
            arguments.AddRange(["--tag", tag]);
        }
        if (metadataOnly)
        {
            arguments.Add("--metadata-only");
        }

        // Act
        var exitCode = await command.Parse(arguments.ToArray()).InvokeAsync();

        // Assert
        exitCode.Should().Be(1);
        console.AllText.Should().Contain("not a valid registry tag").And.Contain("1-128");
        programs.ReceivedCalls().Should().BeEmpty();
        runner.ReceivedCalls().Should().BeEmpty();
        sbx.ReceivedCalls().Should().BeEmpty();
        File.ReadAllText(script).Should().Be("external script");
        File.ReadAllText(certificate).Should().Be("external certificate");
        Directory
            .GetFiles(outside.Path, "*", SearchOption.AllDirectories)
            .Should()
            .BeEquivalentTo([Path.GetFullPath(script), Path.GetFullPath(certificate)]);
        Directory.Exists(paths.StateDirectory).Should().BeFalse();
    }

    public static IEnumerable<object[]> InvalidTags()
    {
        string[] tags =
        [
            "traversal",
            "absolute",
            "unvalued",
            "",
            " ",
            "../outside",
            "..\\outside",
            "/absolute",
            "C:\\absolute",
            "C:relative",
            "\\\\server\\share",
            "a/b",
            "a\\b",
            ".",
            "..",
            "-first",
            ".first",
            "bad:tag",
            "é",
            "line\n",
            "overlong",
        ];
        foreach (var tag in tags)
        foreach (var metadataOnly in new[] { false, true })
        foreach (var configuredTag in new[] { false, true })
        {
            yield return [metadataOnly, configuredTag, tag];
        }
    }

    public static IEnumerable<object[]> ValidTags()
    {
        foreach (var tag in new[] { "A", "0", "_", "_Release.1-rc_2", new string('a', 128) })
        foreach (var metadataOnly in new[] { false, true })
        foreach (var configuredTag in new[] { false, true })
        {
            yield return [metadataOnly, configuredTag, tag];
        }
    }
}
