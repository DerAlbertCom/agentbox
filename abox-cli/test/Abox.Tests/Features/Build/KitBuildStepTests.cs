using Abox.Features.Build;
using Abox.Kits;
using Abox.LocalState;
using Abox.Naming;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class KitBuildStepTests
{
    private readonly BuildServiceFixture _fixture = new();
    private TempDirectory _root => _fixture.Root;
    private ISbxClient _sbx => _fixture.Sbx;
    private BuildStore _store => _fixture.Store;
    private SquadResolver _resolver => _fixture.Resolver;
    private BuildService _service => _fixture.Service;

    [Fact]
    public async Task SbxReceivesTheStagedBuiltKit()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        _root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        BuildScript("built", "echo generated > \"$1/generated.txt\"\n");
        var inspected = new List<string>();
        _sbx.InspectKitAsync(
                Arg.Do<string>(inspected.Add),
                Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyDictionary<string, string>>()
            )
            .Returns(new SbxOutcome(0, ""));

        // Act
        await _service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var kit = Manifest().Kits.Should().ContainSingle(kit => kit.Name == "built").Subject;
        kit.Reference.Should().NotBe(Path.Combine(_root.Path, "kits", "built"));
        Path.GetFileName(kit.Reference).Should().Be("built");
        File.ReadAllText(Path.Combine(kit.Reference, "generated.txt")).Should().Be("generated\n");
        File.Exists(Path.Combine(kit.Reference, "built.yaml")).Should().BeTrue();
        File.Exists(Path.Combine(_root.Path, "kits", "built", "generated.txt")).Should().BeFalse();
        inspected.Should().Contain(kit.Reference);
    }

    [Fact]
    public async Task TheBuildFileGetsTheStagedFolderAndRunsFromTheKitFolder()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        _root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        BuildScript("built", "printf '%s\\n%s\\n' \"$PWD\" \"$1\" > \"$1/invocation.txt\"\n");

        // Act
        await _service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var kit = Manifest().Kits.Should().ContainSingle(kit => kit.Name == "built").Subject;
        File.ReadAllLines(Path.Combine(kit.Reference, "invocation.txt"))
            .Should()
            .Equal(Path.Combine(_root.Path, "kits", "built"), kit.Reference);
    }

    [Fact]
    public async Task AFailingBuildFailsTheBuildWithTheOutputOfTheBuildFile()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        _root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        BuildScript("built", "echo compiling\necho 'syntax error in main.ts' >&2\nexit 3\n");

        // Act
        var build = () => _service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var failure = await build.Should().ThrowAsync<AboxException>();
        failure.WithMessage("*built*compiling*syntax error in main.ts*");
        _store.Load(new SquadBuildIdentity("sample", "ac", "latest")).Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KitsWithoutAnExecutableBuildFilePassThroughUnchanged(bool plainBuildFile)
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [plain]\n");
        _root.File("kits/plain/plain.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        if (plainBuildFile)
        {
            _root.File("kits/plain/build", "#!/bin/sh\nexit 1\n");
        }

        // Act
        await _service.BuildAsync(Request(), CancellationToken.None);

        // Assert
        var source = Path.Combine(_root.Path, "kits", "plain");
        Manifest()
            .Kits.Should()
            .ContainSingle(kit => kit.Name == "plain")
            .Which.Reference.Should()
            .Be(source);
        await _sbx.Received(1)
            .InspectKitAsync(
                source,
                Arg.Any<CancellationToken>(),
                Arg.Any<IReadOnlyDictionary<string, string>>()
            );
    }

    private BuildRequest Request() =>
        new([_resolver.ResolveTeam(_root.Path, "sample")], _root.Path, "latest");

    private BuildManifest Manifest() =>
        _store.Load(new SquadBuildIdentity("sample", "ac", "latest"))!;

    private void BuildScript(string kit, string body) => KitBuildScripts.Write(_root, kit, body);
}
