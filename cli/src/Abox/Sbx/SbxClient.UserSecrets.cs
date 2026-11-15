using System.Security.Cryptography;
using System.Text;
using Abox.Infrastructure;
using Abox.UserSecrets;

namespace Abox.Sbx;

public sealed partial class SbxClient
{
    public Task ValidateUserSecretsDestinationAsync(
        string sandbox,
        IReadOnlyList<string> ids,
        CancellationToken cancellationToken
    )
    {
        foreach (var id in ids)
            UserSecretSelection.ValidateId(id);
        return RunUserSecretsReceiverAsync(sandbox, "check", ids, null, cancellationToken);
    }

    public Task WriteUserSecretsAsync(
        string sandbox,
        string id,
        string json,
        CancellationToken cancellationToken
    )
    {
        UserSecretSelection.ValidateId(id);
        var bytes = new UTF8Encoding(false, true).GetBytes(json);
        var frame = $"{bytes.Length}\n{Convert.ToHexStringLower(SHA256.HashData(bytes))}\n{json}";
        return RunUserSecretsReceiverAsync(sandbox, "write", [id], frame, cancellationToken);
    }

    private async Task RunUserSecretsReceiverAsync(
        string sandbox,
        string mode,
        IReadOnlyList<string> ids,
        string? frame,
        CancellationToken cancellationToken
    )
    {
        ProcessResult result;
        try
        {
            var arguments = new List<string>
            {
                "exec",
                "-i",
                sandbox,
                "sh",
                "-c",
                UserSecretsReceiver,
                "abox-user-secrets",
                mode,
            };
            arguments.AddRange(ids);
            result = await runner.RunAsync(
                new ProcessRequest(
                    ExecutableName,
                    arguments,
                    StandardInput: frame,
                    OutputPolicy: ProcessOutputPolicy.Sensitive
                ),
                cancellationToken
            );
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new AboxException(
                "Cannot transfer user secrets. Check the sandbox runtime and destination permissions; child diagnostics were suppressed."
            );
        }
        if (!result.Succeeded)
            throw new AboxException(
                $"User-secret destination operation failed (exit {result.ExitCode}). Check the application user's HOME/APPDATA, ownership, ordinary file paths, and required shell/coreutils; retry 'abox user-secrets'. Child output is suppressed for safety."
            );
    }

    // Fixed receiver: only validated IDs are argv; payload, byte count and digest are stdin.
    // Resolve the default exec user's environment on every call, never via root or a login shell.
    private const string UserSecretsReceiver = """
        set -eu
        set -f
        umask 077
        uid=$(id -u)
        [ "$uid" -ne 0 ] || exit 64
        if [ "${APPDATA+x}" = x ]; then
            base=$APPDATA
            root="$base/Microsoft/UserSecrets"
        else
            base=${HOME:-}
            root="$base/.microsoft/usersecrets"
        fi
        case "$base" in /*) ;; *) exit 64 ;; esac
        if printf %s "$base" | LC_ALL=C grep -q '[[:cntrl:]]'; then exit 64; fi
        check_path() {
            path=
            old_ifs=$IFS
            IFS=/
            for component in $1; do
                [ -n "$component" ] || continue
                case "$component" in .|..) exit 64 ;; esac
                path="$path/$component"
                [ ! -L "$path" ] || exit 66
                if [ -e "$path" ]; then [ -d "$path" ] || exit 66; fi
            done
            IFS=$old_ifs
        }
        check_store() {
            directory="$root/$1"
            check_path "$directory"
            if [ -e "$root" ]; then [ "$(stat -c %u -- "$root")" = "$uid" ] || exit 66; fi
            if [ -e "$directory" ]; then [ "$(stat -c %u -- "$directory")" = "$uid" ] || exit 66; fi
            destination="$directory/secrets.json"
            [ ! -L "$destination" ] || exit 66
            if [ -e "$destination" ]; then
                [ -f "$destination" ] || exit 66
                [ "$(stat -c %h -- "$destination")" = 1 ] || exit 66
                [ "$(stat -c %u -- "$destination")" = "$uid" ] || exit 66
            fi
        }
        mode=$1
        shift
        [ "$#" -gt 0 ] || exit 64
        for store in "$@"; do check_store "$store"; done
        [ "$mode" = write ] || exit 0
        [ "$#" -eq 1 ] || exit 64
        IFS= read -r expected_size || exit 65
        IFS= read -r expected_digest || exit 65
        case "$expected_size" in ''|*[!0-9]*) exit 65 ;; esac
        case "$expected_digest" in ''|*[!0-9a-f]*) exit 65 ;; esac
        [ "${#expected_digest}" -eq 64 ] || exit 65
        mkdir -p -- "$directory"
        chmod 700 -- "$root" "$directory"
        temporary=$(mktemp "$directory/.secrets.XXXXXX")
        trap 'rm -f -- "$temporary"' EXIT
        trap 'exit 130' HUP INT TERM
        cat > "$temporary"
        [ "$(wc -c < "$temporary" | tr -d '[:space:]')" = "$expected_size" ] || exit 65
        [ "$(sha256sum -- "$temporary" | cut -d ' ' -f 1)" = "$expected_digest" ] || exit 65
        chmod 600 -- "$temporary"
        check_store "$1"
        mv -fT -- "$temporary" "$destination"
        """;
}
