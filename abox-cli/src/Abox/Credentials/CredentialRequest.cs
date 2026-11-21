using Abox.Kits;

namespace Abox.Credentials;

/// <summary>
/// A credential the local kits of a squad ask for with the <c>credential@1</c> capability.
/// </summary>
/// <param name="Service">The name of the service in the host credential store.</param>
/// <param name="Kits">The kits that ask for it.</param>
/// <param name="Domains">The domains its API key is sent to; empty without an <c>apiKey</c> mechanism.</param>
/// <param name="Optional">Whether every kit that asks starts without it.</param>
public sealed record CredentialRequest(
    string Service,
    IReadOnlyList<string> Kits,
    IReadOnlyList<string> Domains,
    bool Optional
)
{
    /// <summary>
    /// The credentials the local kits of the squad ask for, one per service. Kits from a
    /// registry cannot be inspected and are left out.
    /// </summary>
    public static IReadOnlyList<CredentialRequest> Of(ResolvedSquad squad)
    {
        var requests = new List<CredentialRequest>();

        foreach (var kit in squad.Kits)
        {
            if (!kit.IsLocal)
            {
                continue;
            }

            var descriptor = KitDescriptor.Load(kit.Directory!);
            if (descriptor is null)
            {
                continue;
            }

            foreach (var service in descriptor.Services)
            {
                var index = requests.FindIndex(request => request.Service == service.Service);
                if (index < 0)
                {
                    requests.Add(
                        new CredentialRequest(
                            service.Service,
                            [kit.Name],
                            service.Domains,
                            service.Optional
                        )
                    );
                    continue;
                }

                var known = requests[index];
                requests[index] = known with
                {
                    Kits = known.Kits.Contains(kit.Name) ? known.Kits : [.. known.Kits, kit.Name],
                    Domains = known.Domains.Union(service.Domains).ToList(),
                    Optional = known.Optional && service.Optional,
                };
            }
        }

        return requests;
    }
}
