using System.Text.RegularExpressions;
using Abox.Features.Build;

namespace Abox.Naming;

/// <summary>The registry paths shared by published and pulled squad kit sets.</summary>
public static class RegistryImageName
{
    public const string Registry = "rwest.jfrog.io";

    public static bool IsValidRepository(string? repository) =>
        repository is not null
        && Regex.IsMatch(repository, @"\A[a-z0-9]+(?:(?:[._]|__|-+)[a-z0-9]+)*\z");

    public static string SquadRepository(string team, string squad)
    {
        var name = $"agentbox-{ImageName.Tenant}-{team}".ToLowerInvariant();
        var leaf = squad.ToLowerInvariant();
        if (!IsValidRepository(name) || !IsValidRepository(leaf))
        {
            throw new AboxException(
                $"The team/squad '{team}/{squad}' is not a valid registry repository name."
            );
        }

        return name + "/" + leaf;
    }

    /// <summary>The exact JFrog repository scope used for pulling and source trust.</summary>
    public static string PullRepositoryPrefix(string repository)
    {
        if (!IsValidRepository(repository))
        {
            throw new AboxException(
                "'pullRepository' is not a valid repository name. Use one lowercase registry path segment, without a host, slash or tag."
            );
        }

        return $"{Registry}/{repository}/";
    }

    public static string OfSquad(string repository, string team, string squad, string tag)
    {
        var prefix = PullRepositoryPrefix(repository);

        if (!Regex.IsMatch(tag, @"\A[A-Za-z0-9_][A-Za-z0-9_.-]{0,127}\z"))
        {
            throw new AboxException($"'{tag}' is not a valid registry tag.");
        }

        return $"{prefix}{SquadRepository(team, squad)}:{tag}";
    }
}
