namespace Abox.Sbx;

public sealed partial class SbxClient
{
    // A sandbox without HTTPS state retains its original prerequisites. The Python
    // bootstrap never reads stdin and ultimately replaces itself with the exact argv.
    private const string DevelopmentCertificateExecBootstrap = """
        interpreter=$1; source=$2; shift 2
        uid=$(id -u) || exit 78
        case $uid in ''|*[!0-9]*) exit 78 ;; esac
        if [ ! -e "/tmp/abox-https-$uid" ] && [ ! -L "/tmp/abox-https-$uid" ]; then
            exec "$@"
        fi
        exec "$interpreter" -I -c "$source" exec "$@"
        """;
}
