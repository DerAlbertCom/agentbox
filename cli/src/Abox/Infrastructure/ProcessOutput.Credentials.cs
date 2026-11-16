using System.Collections;
using System.Text.RegularExpressions;

namespace Abox.Infrastructure;

public sealed partial class ProcessOutput
{
    private static List<string?> CollectCredentials(ProcessRequest request)
    {
        var credentials = new List<string?> { request.StandardInput };
        CollectEnvironmentCredentials(request, credentials);
        CollectArgumentCredentials(request.Arguments, credentials);
        return credentials;
    }

    private static void CollectEnvironmentCredentials(
        ProcessRequest request,
        List<string?> credentials
    )
    {
        // Keep both inherited and request values: a child may echo either.
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (SensitiveName().IsMatch((string)entry.Key))
                credentials.Add(entry.Value as string);
        }

        if (request.Environment is null)
            return;

        credentials.AddRange(
            request
                .Environment.Where(entry => SensitiveName().IsMatch(entry.Key))
                .Select(entry => entry.Value)
        );
    }

    private static void CollectArgumentCredentials(
        IReadOnlyList<string> arguments,
        List<string?> credentials
    )
    {
        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            CollectUriCredentials(argument, credentials);

            var separator = argument.IndexOf('=');
            if (separator > 0 && SensitiveName().IsMatch(argument[..separator]))
                credentials.Add(argument[(separator + 1)..]);
            else if (
                argument.StartsWith('-')
                && SensitiveName().IsMatch(argument)
                && index + 1 < arguments.Count
            )
                credentials.Add(arguments[index + 1]);
        }
    }

    private static void CollectUriCredentials(string argument, List<string?> credentials)
    {
        if (!Uri.TryCreate(argument, UriKind.Absolute, out var uri))
            return;

        CollectRawAuthorityCredentials(argument, credentials);
        CollectParsedUserInfo(uri.UserInfo, credentials);
        CollectQueryCredentials(uri.Query, credentials);
    }

    private static void CollectRawAuthorityCredentials(string argument, List<string?> credentials)
    {
        // Uri normalizes some escapes; retain credentials exactly as supplied too.
        var authorityStart = argument.IndexOf("://", StringComparison.Ordinal);
        if (authorityStart < 0)
            return;

        var authority = argument[(authorityStart + 3)..].Split(['/', '?', '#'], 2)[0];
        var userInfoEnd = authority.LastIndexOf('@');
        if (userInfoEnd < 0)
            return;

        var userInfo = authority[..userInfoEnd];
        credentials.Add(userInfo);
        var colon = userInfo.IndexOf(':');
        if (colon >= 0)
            credentials.Add(userInfo[(colon + 1)..]);
    }

    private static void CollectParsedUserInfo(string userInfo, List<string?> credentials)
    {
        AddEncodedAndDecoded(userInfo, credentials);
        var password = userInfo.IndexOf(':');
        if (password >= 0)
            AddEncodedAndDecoded(userInfo[(password + 1)..], credentials);
    }

    private static void CollectQueryCredentials(string query, List<string?> credentials)
    {
        foreach (var parameter in query.TrimStart('?').Split('&'))
        {
            var equals = parameter.IndexOf('=');
            if (equals > 0 && SensitiveName().IsMatch(parameter[..equals]))
                AddEncodedAndDecoded(parameter[(equals + 1)..], credentials);
        }
    }

    private static void AddEncodedAndDecoded(string value, List<string?> credentials)
    {
        credentials.Add(value);
        credentials.Add(Uri.UnescapeDataString(value));
    }

    [GeneratedRegex(
        "token|secret|password|passwd|credential|authorization|api[-_]?key|access[-_]?key|private[-_]?key",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex SensitiveName();
}
