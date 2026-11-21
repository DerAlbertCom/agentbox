using Abox.Infrastructure;
using Abox.Kits;
using Abox.LocalState;
using Abox.Sbx;

namespace Abox.Credentials;

/// <summary>
/// Gets the credentials the local kits of a squad ask for ready before a sandbox is
/// created: abox asks for missing values and approvals itself, so sbx never has to.
/// Only the <c>apiKey</c> mechanism is checked; sbx notes the others itself.
/// </summary>
public sealed class SquadCredentials(
    SquadResolver resolver,
    PersonalConfigurations personalConfigurations,
    ISbxClient sbx,
    SbxCredentialBindings bindings,
    SecretValues values,
    IConsole console
)
{
    /// <summary>
    /// Checks that every credential has a stored value and an approval. On a terminal it
    /// asks for what is missing; without one it warns. It never fails the create: sbx
    /// starts without an optional credential and rejects a required one itself.
    /// </summary>
    /// <param name="rootDirectory">The project that holds the squad's sources; null skips the check.</param>
    /// <param name="team">The team the squad belongs to.</param>
    /// <param name="squad">The squad the sandbox is created from.</param>
    public async Task PrepareAsync(
        string? rootDirectory,
        string team,
        string squad,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            return;
        }

        try
        {
            var resolved = resolver.Resolve(
                rootDirectory,
                team,
                squad,
                personalConfigurations.Files
            );
            var requests = CredentialRequest
                .Of(resolved)
                .Where(request => request.Domains.Count > 0)
                .ToList();
            if (requests.Count == 0)
            {
                return;
            }

            var stored = await sbx.ListSecretsAsync(cancellationToken);
            var missing = requests
                .Select(request => Check(request, resolved.Secrets, stored))
                .Where(state => !state.Stored || !state.Approved)
                .ToList();
            if (missing.Count == 0)
            {
                return;
            }

            if (console.IsInteractive)
            {
                await AskAsync(missing, cancellationToken);
            }
            else
            {
                Warn(missing, team, squad);
            }
        }
        catch (AboxException exception)
        {
            console.WriteError(
                $"Warning: the credentials of the squad '{squad}' were not checked: {exception.Message}"
            );
        }
    }

    private CredentialState Check(
        CredentialRequest request,
        IReadOnlyList<SecretDeclaration> secrets,
        HostSecrets stored
    )
    {
        var declaration = secrets.FirstOrDefault(secret => secret.Service == request.Service);
        return new CredentialState(
            request,
            declaration,
            SecretValues.IsStored(request.Service, declaration, stored),
            bindings.Covers(request.Service, request.Domains)
        );
    }

    private async Task AskAsync(
        IReadOnlyList<CredentialState> missing,
        CancellationToken cancellationToken
    )
    {
        console.WriteLine("The kits of the squad ask for credentials that are not ready:");
        foreach (var state in missing)
        {
            console.WriteLine(state.Describe());
        }

        console.WriteLine();

        foreach (var state in missing)
        {
            var name = state.Request.Service;
            if (
                !state.Stored
                && state.Declaration is { } declaration
                && console.Confirm($"Set the secret '{name}' now?")
            )
            {
                await SetAsync(declaration, cancellationToken);
            }

            if (!state.Approved)
            {
                var domains = string.Join(", ", state.Request.Domains);
                if (console.Confirm($"Approve sending the credential '{name}' to {domains}?"))
                {
                    bindings.Approve(name, state.Request.Domains);
                    console.WriteLine($"Approved the credential '{name}' for {domains}.");
                }
                else
                {
                    console.WriteLine($"The sandbox starts without the credential '{name}'.");
                }
            }
        }
    }

    private async Task SetAsync(SecretDeclaration declaration, CancellationToken cancellationToken)
    {
        var name = declaration.Service;
        var value = values.Ask(declaration);
        if (string.IsNullOrEmpty(value))
        {
            console.WriteLine($"The secret '{name}' was not set.");
            return;
        }

        var outcome = await values.StoreAsync(declaration, value, cancellationToken);
        if (!outcome.Succeeded)
        {
            console.WriteError($"The secret '{name}' was not stored: {outcome.Output.Trim()}");
            return;
        }

        console.WriteLine($"Stored the secret '{name}'.");
    }

    private void Warn(IReadOnlyList<CredentialState> missing, string team, string squad)
    {
        console.WriteError(
            "Warning: the kits of the squad ask for credentials that are not ready:"
        );
        foreach (var state in missing)
        {
            console.WriteError(state.Describe());
        }

        foreach (
            var state in missing.Where(state => !state.Stored && state.Declaration is not null)
        )
        {
            console.WriteError(
                $"Set the value with: abox secrets set --team {team} --squad {squad} --service {state.Request.Service}"
            );
        }

        if (missing.Any(state => !state.Approved))
        {
            console.WriteError(
                "An approval is asked for only on a terminal: run abox create on one to approve. The sandbox starts without the credentials that are not approved."
            );
        }
    }

    private sealed record CredentialState(
        CredentialRequest Request,
        SecretDeclaration? Declaration,
        bool Stored,
        bool Approved
    )
    {
        public string Describe()
        {
            var value =
                Stored ? "value stored"
                : Declaration is null ? "value not set (not declared in 'secrets')"
                : "value not set";
            var approval = Approved ? "approved" : "not approved";
            var required = Request.Optional ? "" : ", required";
            return $"  {Request.Service} ({string.Join(", ", Request.Kits)}{required}) for {string.Join(", ", Request.Domains)}: {value}, {approval}";
        }
    }
}
