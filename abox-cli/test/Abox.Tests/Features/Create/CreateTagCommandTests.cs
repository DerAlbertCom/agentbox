using System.Text.Json;
using Abox.Configuration;
using Abox.Features.Create;
using Abox.Health;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sandboxes;
using Abox.Sbx;
using Abox.UserSecrets;
using NSubstitute;

namespace Abox.Tests;

public sealed class CreateTagCommandTests
{
    [Theory]
    [MemberData(nameof(InvalidTags))]
    public async Task UnsafeTagsFailBeforeFolderReadsPreflightOrRuntimeChanges(
        bool local,
        bool configuredTag,
        string input
    )
    {
        // Arrange
        using var root = new TempDirectory();
        using var outside = new TempDirectory();
        var sentinel = outside.File("build.json", "external build");
        root.File("teams/sample/ac.abox", "workload: sample\n");
        root.File("abox.json", "not JSON; invalid tag must be reported first");
        var paths = AboxPaths.For(
            Path.Combine(root.Path, "config"),
            Path.Combine(root.Path, "state")
        );
        var tag = UnsafeTag(input, paths, outside.Path);
        root.File(
            "config/config.yaml",
            "defaultTeam: sample\n"
                + (configuredTag ? "defaultTag: " + JsonSerializer.Serialize(tag) + "\n" : "")
        );
        var config = new Lazy<AboxConfig>(() => AboxConfigLoader.Load(paths));
        var console = new TestConsole();
        var programs = Substitute.For<IHostPrograms>();
        var runner = Substitute.For<IProcessRunner>();
        var sbx = Substitute.For<ISbxClient>();
        var published = Substitute.For<IPublishedSquadSource>();
        var command = new CreateCommand(
            config,
            new Preflight(console, sbx, programs),
            new Lazy<SquadResolver>(() => new SquadResolver(config.Value)),
            new Lazy<SandboxCreator>(() =>
                TestServiceGraphs.Creator(
                    new SandboxRecordStore(paths),
                    paths,
                    sbx,
                    console,
                    new LocalSquadSource(
                        new BuildStore(paths),
                        new LocalWorkloadImages(programs, new OciRegistry(runner), console),
                        console
                    ),
                    new RemoteSquadSource(config.Value, sbx, published),
                    new UserSecretsCopy(config.Value, new UserSecretsHost(null, null), sbx)
                )
            ),
            console
        );
        var arguments = new List<string> { "--location", root.Path, "--squad", "ac", "--yes" };
        if (local)
            arguments.AddRange(["--local", "--root", root.Path]);
        if (!configuredTag)
            arguments.AddRange(["--tag", tag]);

        // Act
        var exitCode = await command.Parse(arguments.ToArray()).InvokeAsync();

        // Assert
        exitCode.Should().Be(1);
        console.AllText.Should().Contain("not a valid registry tag").And.Contain("1-128");
        programs.ReceivedCalls().Should().BeEmpty();
        runner.ReceivedCalls().Should().BeEmpty();
        sbx.ReceivedCalls().Should().BeEmpty();
        published.ReceivedCalls().Should().BeEmpty();
        File.ReadAllText(sentinel).Should().Be("external build");
        Directory.GetFiles(outside.Path, "*", SearchOption.AllDirectories).Should().Equal(sentinel);
        Directory.Exists(paths.StateDirectory).Should().BeFalse();
    }

    private static string UnsafeTag(string input, AboxPaths paths, string outside) =>
        input switch
        {
            "traversal" => Path.GetRelativePath(Path.Combine(paths.BuildsDirectory, "ac"), outside),
            "absolute" => outside,
            "overlong" => new string('a', 129),
            _ => input,
        };

    public static IEnumerable<object[]> InvalidTags()
    {
        foreach (var tag in new[] { "traversal", "absolute", "", " ", "overlong", "a/b", "a\\b" })
        foreach (var local in new[] { false, true })
        foreach (var configuredTag in new[] { false, true })
        {
            yield return [local, configuredTag, tag];
        }
    }
}
