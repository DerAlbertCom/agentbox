namespace Abox.Kits;

/// <summary>How a secret value is stored in the host store.</summary>
public enum SecretEncoding
{
    /// <summary>The value is stored as it is.</summary>
    Plain,

    /// <summary>The value is stored base64 encoded.</summary>
    Base64,
}

/// <summary>
/// One part of a secret that is built from several inputs.
/// </summary>
/// <param name="Name">The name of the part.</param>
/// <param name="Prompt">What the user is asked for.</param>
public sealed record SecretInput(string Name, string? Prompt);

/// <summary>
/// A secret of the host, as a configuration declares it.
/// </summary>
/// <param name="Service">The name of the secret in the host store. The kits name the same service.</param>
/// <param name="Prompt">What the user is asked for.</param>
/// <param name="Inputs">The parts the value is built from. Without them, one value is asked for.</param>
/// <param name="Separator">How the parts are joined.</param>
/// <param name="Encoding">How the value is stored.</param>
/// <param name="Host">The host of a service sbx does not know.</param>
/// <param name="EnvironmentVariable">The variable the agents read, for a service sbx does not know.</param>
public sealed record SecretDeclaration(
    string Service,
    string? Prompt = null,
    IReadOnlyList<SecretInput>? Inputs = null,
    string Separator = ":",
    SecretEncoding Encoding = SecretEncoding.Plain,
    string? Host = null,
    string? EnvironmentVariable = null
)
{
    /// <summary>Whether the secret is one sbx does not know.</summary>
    public bool IsCustom => Host is not null || EnvironmentVariable is not null;
}
