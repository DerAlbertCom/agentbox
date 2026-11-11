using System.CommandLine;
using Abox.Configuration;
using Abox.Infrastructure;
using Abox.Kits;

namespace Abox.Features.Publish;

/// <summary><c>abox publish</c>: publishes the local kit sets of a team to JFrog.</summary>
public sealed class PublishCommand : Command
{
    public PublishCommand(
        Lazy<AboxConfig> config,
        Lazy<SquadResolver> resolver,
        PublishService publish,
        IConsole console
    )
        : base("publish", "Publish the local kit sets of a team to JFrog.")
    {
        var teams = new Option<string[]>("--team")
        {
            Description =
                "The team to publish. Can be repeated. Defaults to the team of the user configuration.",
            AllowMultipleArgumentsPerToken = false,
        };
        var root = new Option<string>("--root")
        {
            Description =
                "The directory that holds the teams folder. Defaults to the current directory.",
        };
        var tag = new Option<string>("--tag")
        {
            Description =
                "The local build and registry tag. Defaults to the tag of the user configuration, latest.",
        };
        var builder = new Option<string>("--builder")
        {
            Description =
                "The Buildx builder to use. Defaults to Docker's selected builder (including BUILDX_BUILDER).",
        };
        Options.Add(teams);
        Options.Add(root);
        Options.Add(tag);
        Options.Add(builder);

        SetAction(
            async (parseResult, cancellationToken) =>
                await CommandHandling.RunAsync(
                    async () =>
                    {
                        var settings = config.Value;
                        var named = parseResult.GetValue(teams);
                        var wanted =
                            named is { Length: > 0 } ? named
                            : settings.DefaultTeam is { } fallback ? [fallback]
                            : [];
                        if (wanted.Length == 0)
                        {
                            throw new AboxException(
                                "No team is given. Give --team <name> or set 'defaultTeam' in the user configuration."
                            );
                        }

                        var resolved = wanted
                            .Select(team =>
                                resolver.Value.ResolveTeam(
                                    parseResult.GetValue(root) ?? Directory.GetCurrentDirectory(),
                                    team
                                )
                            )
                            .ToList();
                        return await publish.PublishAsync(
                            new PublishRequest(
                                resolved,
                                parseResult.GetValue(tag) ?? settings.DefaultTag,
                                parseResult.GetValue(builder)
                            ),
                            cancellationToken
                        );
                    },
                    console
                )
        );
    }
}
