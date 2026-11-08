using Abox.Build;
using Abox.Infrastructure;
using Abox.Kits;
using Abox.Sbx;

namespace Abox.Services;

/// <summary>What to do with the secrets of a squad.</summary>
/// <param name="Squad">The squad whose secrets are meant.</param>
/// <param name="RootDirectory">The directory the squad's configurations live in.</param>
/// <param name="Services">The services to act on. All of them when the list is empty.</param>
public sealed record SecretsRequest(
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
            var state = IsStored(name, declaration, stored) ? "stored" : "not set";

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

            var value = ReadValue(declaration);
            if (value is null)
            {
                console.WriteError($"The service '{name}' was not set.");
                failed = true;
                continue;
            }

            var outcome = declaration.IsCustom
                ? await sbx.SetCustomSecretAsync(
                    declaration.Service,
                    declaration.Host,
                    declaration.EnvironmentVariable,
                    value,
                    cancellationToken
                )
                : await sbx.SetSecretAsync(declaration.Service, value, cancellationToken);

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
                name,
                declaration?.IsCustom == true ? declaration.Host : null,
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

    /// <summary>
    /// Whether the value of a service is on the host. A service sbx does not know is
    /// stored under its host, not under its name.
    /// </summary>
    private static bool IsStored(string name, SecretDeclaration? declaration, HostSecrets stored)
    {
        if (declaration?.IsCustom == true && declaration.Host is not null)
        {
            return stored.Custom.Any(custom => custom.Targets.Contains(declaration.Host));
        }

        return stored.Services.Contains(name);
    }

    /// <summary>
    /// Asks for the value of a secret: one value, or the parts it is built from.
    /// </summary>
    private string? ReadValue(SecretDeclaration declaration)
    {
        var parts = new List<string>();

        if (declaration.Inputs is { Count: > 0 })
        {
            foreach (var input in declaration.Inputs)
            {
                var prompt = input.Prompt ?? $"The {input.Name} of '{declaration.Service}'";
                var part = console.ReadSecret($"{prompt}: ");
                if (part is null)
                {
                    return null;
                }

                parts.Add(part);
            }
        }
        else
        {
            var prompt = declaration.Prompt ?? $"The secret '{declaration.Service}'";
            var value = console.ReadSecret($"{prompt}: ");
            if (value is null)
            {
                return null;
            }

            parts.Add(value);
        }

        var joined = string.Join(declaration.Separator, parts);

        return declaration.Encoding == SecretEncoding.Base64
            ? Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(joined))
            : joined;
    }

    /// <summary>The services the kits of the squad ask for, with the kits that ask.</summary>
    private IReadOnlyDictionary<string, IReadOnlyList<string>> ServicesOf(ResolvedSquad squad)
    {
        var services = new Dictionary<string, List<string>>(StringComparer.Ordinal);

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
                if (!services.TryGetValue(service.Service, out var kits))
                {
                    kits = [];
                    services[service.Service] = kits;
                }

                if (!kits.Contains(kit.Name))
                {
                    kits.Add(kit.Name);
                }
            }
        }

        return services.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyList<string>)entry.Value,
            StringComparer.Ordinal
        );
    }

    private ResolvedSquad Resolve(SecretsRequest request) =>
        resolver.Resolve(request.RootDirectory, request.Squad, personalConfigurations.Files);
}
