namespace Abox.UserSecrets;

/// <summary>The host's .NET store convention, independent of abox's config directory.</summary>
public sealed class UserSecretsHost(string? appData, string? home)
{
    public static UserSecretsHost Create() =>
        new(
            Environment.GetEnvironmentVariable("APPDATA"),
            Environment.GetEnvironmentVariable("HOME")
        );

    internal string StorePath(string id)
    {
        var root = appData ?? home;
        if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            throw new AboxException(
                "Cannot resolve the host user-secret directory. Set an absolute APPDATA or HOME; APPDATA takes precedence, including on Unix."
            );
        return appData is not null
            ? Path.Combine(root, "Microsoft", "UserSecrets", id, "secrets.json")
            : Path.Combine(root, ".microsoft", "usersecrets", id, "secrets.json");
    }
}
