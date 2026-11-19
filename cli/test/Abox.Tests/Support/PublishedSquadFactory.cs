using Abox.LocalState;
using Abox.Naming;
using Abox.Sandboxes;
using NSubstitute;

namespace Abox.Tests;

internal static class PublishedSquadFactory
{
    public const string Digest =
        "sha256:1111111111111111111111111111111111111111111111111111111111111111";

    public static IPublishedSquadSource Source(List<ManifestMcp>? mcp = null)
    {
        var source = Substitute.For<IPublishedSquadSource>();
        source
            .LoadAsync(
                Arg.Any<string>(),
                Arg.Any<SquadBuildIdentity>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call => new PublishedSquad(
                call.ArgAt<string>(0).Split(':')[0] + "@" + Digest,
                new BuildManifest
                {
                    Squad = call.Arg<SquadBuildIdentity>().Squad,
                    Tag = call.Arg<SquadBuildIdentity>().Tag,
                    CreatedAt = DateTimeOffset.UtcNow,
                    State =
                    [
                        new ManifestState
                        {
                            SandboxPath = "/home/agent/.published",
                            RelativePath = ".published",
                        },
                    ],
                    Mcp = mcp ?? [],
                }
            ));
        return source;
    }
}
