using Abox.Credentials;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sbx;

namespace Abox.Features.Secrets;

/// <summary>What to do with the secrets of a squad.</summary>
/// <param name="Team">The team the squad belongs to.</param>
/// <param name="Squad">The squad whose secrets are meant.</param>
/// <param name="RootDirectory">The directory of the project that holds the teams folder.</param>
/// <param name="Services">The services to act on. All of them when the list is empty.</param>
public sealed record SecretsRequest(
    string Team,
    string Squad,
    string RootDirectory,
    IReadOnlyList<string> Services
);

/// <summary>
/// The secrets of the host: the kits ask for a service, the configuration says how the
/// value of that service is obtained, and sbx keeps it.
/// </summary>
public sealed class SecretsService(
    SquadResolver resolver,
    PersonalConfigurations personalConfigurations,
    ISbxClient sbx,
    SecretValues values,
    IConsole console
)
{
    /// <summary>Lists the services of the squad and whether they are stored.</summary>
    public async Task<int> ListAsync(SecretsRequest request, CancellationToken cancellationToken)
    {
        var squad = Resolve(request);
        var services = ServicesOf(squad);
        var stored = await sbx.ListSecretsAsync(cancellationToken);

        var names = services
            .Keys.Union(squad.Secrets.Select(secret => secret.Service))
            .Order()
            .ToList();

        console.WriteLine(
            $"{"SERVICE".PadRight(20)} {"KITS".PadRight(20)} {"STATE".PadRight(10)} NOTE"
        );

        foreach (var name in names)
        {
            var kits = services.TryGetValue(name, out var kitNames)
                ? string.Join(", ", kitNames)
                : "-";
            var declaration = squad.Secrets.FirstOrDefault(secret => secret.Service == name);
            var declared = declaration is not null;
            var state = SecretValues.IsStored(name, declaration, stored) ? "stored" : "not set";

            console.WriteLine(
                $"{name.PadRight(20)} {kits.PadRight(20)} {state.PadRight(10)} {(declared ? "" : "not declared")}".TrimEnd()
            );
        }

        return 0;
    }

    /// <summary>Asks for the values of the services and stores them on the host.</summary>
    public async Task<int> SetAsync(SecretsRequest request, CancellationToken cancellationToken)
    {
        var squad = Resolve(request);
        var services = ServicesOf(squad);

        var wanted =
            request.Services.Count > 0
                ? request.Services
                : squad.Secrets.Select(secret => secret.Service).ToList();

        if (wanted.Count == 0)
        {
            console.WriteError("There are no secrets to set.");
            return 1;
        }

        var failed = false;

        foreach (var name in wanted)
        {
            var declaration = squad.Secrets.FirstOrDefault(secret => secret.Service == name);
            if (declaration is null)
            {
                console.WriteError(
                    $"The service '{name}' is not declared. Add it to the 'secrets' section of the configuration."
                );
                failed = true;
                continue;
            }

            if (!services.ContainsKey(name))
            {
                console.WriteLine($"Note: no kit of the squad asks for the service '{name}'.");
            }

            var value = values.Ask(declaration);
            if (value is null)
            {
                console.WriteError($"The service '{name}' was not set.");
                failed = true;
                continue;
            }

            var outcome = await values.StoreAsync(declaration, value, cancellationToken);

            if (!outcome.Succeeded)
            {
                console.WriteError($"The secret '{name}' was not stored: {outcome.Output.Trim()}");
                failed = true;
                continue;
            }

            console.WriteLine($"Stored the secret '{name}'.");
        }

        return failed ? 1 : 0;
    }

    /// <summary>Removes the secrets of the services from the host.</summary>
    public async Task<int> RemoveAsync(SecretsRequest request, CancellationToken cancellationToken)
    {
        var squad = Resolve(request);

        var wanted =
            request.Services.Count > 0
                ? request.Services
                : squad.Secrets.Select(secret => secret.Service).ToList();

        var failed = false;

        foreach (var name in wanted)
        {
            var declaration = squad.Secrets.FirstOrDefault(secret => secret.Service == name);
            var outcome = await sbx.RemoveSecretAsync(
                declaration ?? new SecretDeclaration(name),
                cancellationToken
            );
            if (!outcome.Succeeded)
            {
                console.WriteError($"The secret '{name}' was not removed: {outcome.Output.Trim()}");
                failed = true;
                continue;
            }

            console.WriteLine($"Removed the secret '{name}'.");
        }

        return failed ? 1 : 0;
    }

    /// <summary>The services the local kits of the squad ask for, with the kits that ask.</summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<string>> ServicesOf(
        ResolvedSquad squad
    ) =>
        CredentialRequest
            .Of(squad)
            .ToDictionary(
                request => request.Service,
                request => request.Kits,
                StringComparer.Ordinal
            );

    private ResolvedSquad Resolve(SecretsRequest request) =>
        resolver.Resolve(
            request.RootDirectory,
            request.Team,
            request.Squad,
            personalConfigurations.Files
        );
}
