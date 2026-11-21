using Abox.Infrastructure;
using Abox.Kits;
using Abox.Sbx;

namespace Abox.Credentials;

/// <summary>
/// The values of the services on the host: asked for as the <c>secrets</c> section of
/// the squad declares, and kept by <c>sbx secret</c>.
/// </summary>
public sealed class SecretValues(ISbxClient sbx, IConsole console)
{
    /// <summary>
    /// Whether the value of a service is on the host. A service sbx does not know is
    /// stored under its host or environment variable, not under its service name.
    /// </summary>
    public static bool IsStored(string name, SecretDeclaration? declaration, HostSecrets stored)
    {
        var (kind, value) = (declaration ?? new SecretDeclaration(name)).Identity;
        return kind switch
        {
            SecretIdentityKind.Host => stored.Custom.Any(custom => custom.Targets.Contains(value)),
            SecretIdentityKind.EnvironmentVariable => stored.Custom.Any(custom =>
                custom.EnvironmentVariable == value
            ),
            _ => stored.Services.Contains(value),
        };
    }

    /// <summary>
    /// Asks for the value of a secret: one value, or the parts it is built from. Returns
    /// null when there is nothing to read.
    /// </summary>
    public string? Ask(SecretDeclaration declaration)
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

    /// <summary>Stores the value of a service: a custom secret for a service sbx does not know.</summary>
    public Task<SbxOutcome> StoreAsync(
        SecretDeclaration declaration,
        string value,
        CancellationToken cancellationToken
    ) =>
        declaration.IsCustom
            ? sbx.SetCustomSecretAsync(
                declaration.Host,
                declaration.EnvironmentVariable,
                value,
                cancellationToken
            )
            : sbx.SetSecretAsync(declaration.Service, value, cancellationToken);
}
