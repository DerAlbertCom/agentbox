using Abox.Configuration;
using Abox.Features.Build;
using Abox.Features.Publish;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sbx;
using NSubstitute;

namespace Abox.Tests;

public sealed class BuiltKitPublicationTests
{
    private readonly PublishServiceFixture _fixture = new();
    private TempDirectory _root => _fixture.Root;

    [Fact]
    public async Task PublicationPushesTheStagedBuiltKit()
    {
        // Arrange
        _root.File("teams/sample/ac.abox", "workload: sample\nkits: [built]\n");
        _root.File(
            "teams/sample/sample.workload",
            "template: example.com/sample:1\npublishRepository: sample-images-local\n"
        );
        _root.File("kits/built/built.yaml", "schemaVersion: \"3\"\nkind: mixin\n");
        KitBuildScripts.Write(_root, "built", "echo generated > \"$1/generated.txt\"\n");
        var resolver = new SquadResolver(new AboxConfig());
        var team = resolver.ResolveTeam(_root.Path, "sample");
        await TestServiceGraphs
            .Build(
                resolver,
                new PersonalConfigurations(AboxPaths.For(_root.Path, _root.Path)),
                _fixture.Store,
                LocalWorkloadFactory.Images(),
                Substitute.For<ISbxClient>(),
                _fixture.Console
            )
            .BuildAsync(
                new BuildRequest(
                    [team],
                    _root.Path,
                    "latest",
                    Verify: false,
                    IncludePersonalConfigurations: false
                ),
                CancellationToken.None
            );
        var pushedKits = new List<string>();
        _fixture
            .Runner.When(runner =>
                runner.RunAsync(
                    Arg.Is<ProcessRequest>(request =>
                        request.Arguments.Contains("--push")
                        && request.Arguments.Any(argument => argument.Contains("/components/kit-0"))
                    ),
                    Arg.Any<CancellationToken>()
                )
            )
            .Do(call => pushedKits.Add(ReadGenerated(call.Arg<ProcessRequest>().Arguments[^1])));

        // Act
        var exitCode = await _fixture.Service.PublishAsync(
            new PublishRequest([team], "latest"),
            CancellationToken.None
        );

        // Assert
        exitCode.Should().Be(0);
        pushedKits.Should().Equal("generated\n");
    }

    private static string ReadGenerated(string context)
    {
        var generated = Path.Combine(context, "generated.txt");
        return File.Exists(generated) ? File.ReadAllText(generated) : "(missing)";
    }
}
