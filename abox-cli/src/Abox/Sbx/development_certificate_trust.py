"""Install one exact PUBLIC leaf in the selected sandbox's Debian-style store.

This program is invoked only by SbxClient with root exec. It never receives a PFX,
password or private key, never installs packages, and never modifies host trust.
"""
import fcntl
import os
import re
import secrets
import signal
import ssl
import stat
import subprocess
import sys


def require(condition):
    if not condition:
        raise ValueError('unsafe or unsupported sandbox trust destination')


def check_mounts(path):
    with open('/proc/self/mountinfo', encoding='utf-8') as source:
        for line in source:
            fields = line.split()
            require(len(fields) >= 10)
            mount = re.sub(r'\\([0-7]{3})', lambda match: chr(int(match[1], 8)), fields[4])
            if mount != '/':
                require(not (path == mount or path.startswith(mount + '/') or mount.startswith(path + '/')))


def directory(path):
    check_mounts(path)
    current = '/'
    info = os.lstat(current)
    require(stat.S_ISDIR(info.st_mode) and info.st_uid == 0 and info.st_mode & 0o022 == 0)
    for part in path.strip('/').split('/'):
        current = os.path.join(current, part)
        info = os.lstat(current)
        require(stat.S_ISDIR(info.st_mode) and info.st_uid == 0 and info.st_mode & 0o022 == 0)


def ordinary(info, mode):
    require(stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and stat.S_IMODE(info.st_mode) == mode)


def contents(root, certificate):
    for name in os.listdir(root):
        require(name in (certificate, '.lock') or re.fullmatch(r'\.transfer-[0-9a-f]{32}\.tmp', name) is not None)
        info = os.stat(name, dir_fd=root, follow_symlinks=False)
        # A killed public-file writer can leave its narrowly named temporary
        # after fchmod but before rename. Both modes are root-write-only.
        mode = 0o644 if name == certificate or (name.startswith('.transfer-') and stat.S_IMODE(info.st_mode) == 0o644) else 0o600
        ordinary(info, mode)


def run():
    require(sys.platform == 'linux' and sys.version_info >= (3, 8) and os.geteuid() == 0)
    require(len(sys.argv) == 2 and re.fullmatch(r'[1-9][0-9]{0,9}', sys.argv[1]) is not None)
    public = sys.stdin.buffer.read(16385)
    require(re.fullmatch(rb'-----BEGIN CERTIFICATE-----\r?\n[A-Za-z0-9+/=\r\n]+-----END CERTIFICATE-----\s*', public) is not None)
    require(len(public) <= 16384)
    # Validate that this is a certificate, not merely arbitrary base64. Only the
    # already validated host identity's public export is sent by the host adapter.
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.load_verify_locations(cadata=public.decode('ascii'))
    base = '/usr/local/share/ca-certificates'
    directory(base)
    directory('/etc/ssl/certs')
    bundle = '/etc/ssl/certs/ca-certificates.crt'
    info = os.lstat(bundle)
    ordinary(info, 0o644)
    directory('/etc/ca-certificates/update.d')
    directory('/usr/sbin')
    update = '/usr/sbin/update-ca-certificates'
    info = os.lstat(update)
    require(stat.S_ISREG(info.st_mode) and info.st_uid == 0 and info.st_nlink == 1 and info.st_mode & 0o022 == 0 and info.st_mode & 0o111 != 0)
    name = 'abox-development-https-' + sys.argv[1]
    path = base + '/' + name
    check_mounts(path)
    os.umask(0o077)
    try:
        os.mkdir(path, 0o755)
        os.chmod(path, 0o755)
    except FileExistsError:
        pass
    directory(path)
    require(stat.S_IMODE(os.lstat(path).st_mode) == 0o755)
    root = os.open(path, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    temporary = '.transfer-' + secrets.token_hex(16) + '.tmp'
    certificate = name + '.crt'
    try:
        lock = os.open('.lock', os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600, dir_fd=root)
        try:
            ordinary(os.fstat(lock), 0o600)
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            contents(root, certificate)
            for entry in os.listdir(root):
                if entry.startswith('.transfer-'):
                    os.unlink(entry, dir_fd=root)
            output = os.open(temporary, os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600, dir_fd=root)
            with os.fdopen(output, 'wb') as destination:
                destination.write(public)
                destination.flush()
                os.fchmod(destination.fileno(), 0o644)
                os.fsync(destination.fileno())
            os.replace(temporary, certificate, src_dir_fd=root, dst_dir_fd=root)
            os.fsync(root)
            # Always update, including on an identical retry: a prior invocation
            # may have died after rename but before the system-store update.
            result = subprocess.run([update], stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL,
                                    stderr=subprocess.DEVNULL, timeout=30,
                                    env={'PATH': '/usr/sbin:/usr/bin:/sbin:/bin', 'LC_ALL': 'C'})
            require(result.returncode == 0)
        finally:
            os.close(lock)
    finally:
        if temporary in os.listdir(root):
            os.unlink(temporary, dir_fd=root)
        os.close(root)


def interrupted(signum, frame):
    raise SystemExit(128 + signum)


for event in (signal.SIGHUP, signal.SIGINT, signal.SIGTERM):
    if signal.getsignal(event) != signal.SIG_IGN:
        signal.signal(event, interrupted)

try:
    run()
except Exception:
    # The host provides fixed actionable guidance; do not leak process diagnostics.
    sys.exit(1)
