using Abox.Configuration;
using Abox.Features.Build;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuiltKitCreationTests
{
    private readonly SandboxCreatorFixture _fixture = new();
    private TempDirectory _root => _fixture.Root;

    [Fact]
    public async Task LocalCreationUsesTheStagedBuiltKit()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        _root.File("teams/sample/sample.workload", "template: example.com/sample:1\n");
        _root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        KitBuildScripts.Write(_root, "built", "echo generated > \"$1/generated.txt\"\n");
        var resolver = new SquadResolver(new AboxConfig());
        var team = resolver.ResolveTeam(_root.Path, "sample");
        await TestServiceGraphs
            .Build(
                resolver,
                new PersonalConfigurations(_fixture.Paths),
                _fixture.Store,
                LocalWorkloadFactory.Images(),
                _fixture.Sbx,
                _fixture.Console
            )
            .BuildAsync(
                new BuildRequest([team], _root.Path, "latest", Verify: false),
                CancellationToken.None
            );
        var mixins = new List<string>();
        _fixture
            .Sbx.CreateAsync(
                Arg.Do<SbxCreateRequest>(request => mixins.AddRange(request.Mixins)),
                Arg.Any<CancellationToken>()
            )
            .Returns(new SbxOutcome(0, string.Empty));

        // Act
        var exitCode = await _fixture.Service.CreateAsync(
            _fixture.Request() with
            {
                Team = team,
                Location = _root.CreateDirectory("location"),
            },
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        mixins.Should().NotContain(Path.Combine(_root.Path, "kits", "built"));
        mixins
            .Should()
            .ContainSingle(mixin => Path.GetFileName(mixin) == "built")
            .Which.Should()
            .Match(mixin => File.Exists(Path.Combine(mixin, "generated.txt")));
    }
}
