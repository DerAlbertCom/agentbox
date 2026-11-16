digest=$1; shift
uid=$(id -u) || exit 78
case $uid in ''|*[!0-9]*) exit 78 ;; esac
if [ ! -e "/tmp/abox-https-$uid" ] && [ ! -L "/tmp/abox-https-$uid" ]; then
    exec "$@"
fi
# Python is a configuration reader, never the application's exec parent. In
# particular CPython's SIGPIPE/SIGXFSZ and locale initialization cannot leak.
config=$(python3 -I -c '
import hashlib,os,stat,sys
try:
 p="/tmp/abox-https-"+str(os.geteuid())
 d=os.open(p,os.O_RDONLY|os.O_DIRECTORY|os.O_NOFOLLOW)
 s=os.fstat(d)
 assert s.st_uid==os.geteuid() and stat.S_IMODE(s.st_mode)==0o700
 f=os.open("bootstrap.py",os.O_RDONLY|os.O_NOFOLLOW,dir_fd=d)
 s=os.fstat(f)
 assert stat.S_ISREG(s.st_mode) and s.st_uid==os.geteuid() and s.st_nlink==1 and stat.S_IMODE(s.st_mode)==0o600 and s.st_size<131072
 with os.fdopen(f,"rb") as r: source=r.read(131072)
 assert hashlib.sha256(source).hexdigest()==sys.argv[1]
 os.close(d)
except Exception:
 sys.stderr.write("abox HTTPS: protected bootstrap missing or changed (also possible after a CLI upgrade). Launches fail closed; run abox ports --location <folder> --refresh-https-bootstrap with this CLI version to refresh only the verified reader, then retry. This requires valid protected private activation, not host export, project discovery or root trust. No installed unverified code was executed.\n")
 sys.exit(78)
sys.argv=["abox-https","environment"]+sys.argv[2:]
exec(compile(source,"<abox-https>","exec"))
' "$digest" "$@") || exit $?
password=${config%% *}; node=${config#* }
case $password in ''|*[!0-9a-f]*) exit 78 ;; esac
[ "${#password}" = 64 ] || exit 78
case $node in node-*.pem|-) ;; *) exit 78 ;; esac
case $node in *[!0-9a-z.-]*) exit 78 ;; esac
ASPNETCORE_Kestrel__Certificates__Default__Path="/tmp/abox-https-$uid/active.pfx"
ASPNETCORE_Kestrel__Certificates__Default__Password=$password
if [ "$node" != '-' ]; then
    NODE_EXTRA_CA_CERTS="/tmp/abox-https-$uid/$node"
    export NODE_EXTRA_CA_CERTS
fi
export ASPNETCORE_Kestrel__Certificates__Default__Path ASPNETCORE_Kestrel__Certificates__Default__Password
exec "$@"
