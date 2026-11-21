namespace Abox.Naming;

/// <summary>A squad build's portable identity, shared by local storage and registry operations.</summary>
public readonly record struct SquadBuildIdentity(string Team, string Squad, string Tag)
{
    public void Validate()
    {
        RegistryImageName.ValidateTag(Tag);
        _ = Repository;
    }

    /// <summary>The lowercased registry repository path, without registry host or tag.</summary>
    public string Repository
    {
        get
        {
            var name = TeamRepository(Team).ToLowerInvariant();
            var leaf = Squad.ToLowerInvariant();
            if (
                !RegistryImageName.IsValidRepository(name)
                || !RegistryImageName.IsValidRepository(leaf)
            )
                throw new AboxException(
                    $"The team/squad '{Team}/{Squad}' is not a valid registry repository name."
                );
            return name + "/" + leaf;
        }
    }

    internal static string TeamRepository(string team) => $"agentic-box-{ImageName.Tenant}-{team}";
}
