"""Private staging only: this receiver neither establishes trust nor configures Kestrel."""

import base64
import fcntl
import hashlib
import json
import os
import re
import signal
import stat
import sys


def require(condition):
    if not condition:
        raise ValueError("unsafe certificate staging")


def ordinary_file(info, uid):
    require(stat.S_ISREG(info.st_mode) and info.st_uid == uid)
    require(stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1)


def read_payload():
    source = sys.stdin.buffer
    require(source.readline(64) == b"ABOX-CERTIFICATE-1\n")
    size = source.readline(32)
    require(re.fullmatch(rb"[1-9][0-9]{0,6}\n", size) is not None)
    size = int(size)
    require(size <= 1500000)
    digest = source.readline(128)
    require(re.fullmatch(rb"[0-9a-f]{64}\n", digest) is not None)
    payload = source.read(size + 1)
    require(len(payload) == size and hashlib.sha256(payload).hexdigest().encode() == digest[:-1])
    data = json.loads(payload)
    require(isinstance(data, dict) and set(data) == {"version", "pfx", "password"})
    require(type(data["version"]) is int and data["version"] == 1)
    require(isinstance(data["password"], str) and 0 < len(data["password"]) <= 1024)
    require("\x00" not in data["password"] and isinstance(data["pfx"], str))
    pfx = base64.b64decode(data["pfx"], validate=True)
    require(0 < len(pfx) <= 1024 * 1024)
    return payload


def check_mounts(root):
    # No HOME/workspace path guessing: require the container root filesystem, not
    # a host-persistent /tmp, ancestor bind mount or mounted child in our directory.
    with open("/proc/self/mountinfo", encoding="utf-8") as mounts:
        for line in mounts:
            fields = line.split()
            require(len(fields) >= 10)
            path = re.sub(r"\\([0-7]{3})", lambda match: chr(int(match[1], 8)), fields[4])
            if path != "/":
                require(not (root == path or root.startswith(path + "/") or path.startswith(root + "/")))


def open_root(uid, create):
    root = "/tmp/abox-https-" + str(uid)
    for path in ("/", "/tmp"):
        info = os.lstat(path)
        require(stat.S_ISDIR(info.st_mode) and info.st_uid == 0)
        if path == "/tmp":
            require(stat.S_IMODE(info.st_mode) == 0o1777)
        else:
            require(info.st_mode & 0o022 == 0)
    check_mounts(root)
    if create:
        try:
            os.mkdir(root, 0o700)
        except FileExistsError:
            pass
    elif not os.path.lexists(root):
        return None
    fd = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    info = os.fstat(fd)
    try:
        require(info.st_uid == uid and stat.S_IMODE(info.st_mode) == 0o700)
    except BaseException:
        os.close(fd)
        raise
    return fd


def check_contents(root, uid):
    for name in os.listdir(root):
        require(name in ("active.json", ".lock") or re.fullmatch(r"\.transfer-[0-9a-f]{32}\.tmp", name) is not None)
        ordinary_file(os.stat(name, dir_fd=root, follow_symlinks=False), uid)


def run():
    require(sys.platform == "linux" and sys.version_info >= (3, 8))
    uid = os.geteuid()
    require(uid != 0 and len(sys.argv) == 3)
    mode, transfer = sys.argv[1:]
    require(mode in ("stage", "cleanup") and re.fullmatch(r"[0-9a-f]{32}", transfer) is not None)
    payload = read_payload() if mode == "stage" else None
    os.umask(0o077)
    root = open_root(uid, mode == "stage")
    if root is None:
        return
    temporary = ".transfer-" + transfer + ".tmp"
    try:
        lock = os.open(".lock", os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600, dir_fd=root)
        try:
            ordinary_file(os.fstat(lock), uid)
            # Do not let a dead/malicious holder hang cancellation recovery.
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            check_contents(root, uid)
            if mode == "cleanup":
                if temporary in os.listdir(root):
                    os.unlink(temporary, dir_fd=root)
                return
            # Serialized writers can safely remove only validated, abandoned abox
            # staging files. The current writer has not created its file yet.
            for name in os.listdir(root):
                if name.startswith(".transfer-"):
                    os.unlink(name, dir_fd=root)
            output = os.open(temporary, os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600, dir_fd=root)
            try:
                with os.fdopen(output, "wb") as destination:
                    destination.write(payload)
                    destination.flush()
                    os.fsync(destination.fileno())
                check_contents(root, uid)
                # Password + unchanged PFX bytes are ONE atomic replacement. This
                # is deliberately not a pair of independently renamed files.
                os.replace(temporary, "active.json", src_dir_fd=root, dst_dir_fd=root)
                os.fsync(root)
            finally:
                if temporary in os.listdir(root):
                    os.unlink(temporary, dir_fd=root)
        finally:
            os.close(lock)
    finally:
        os.close(root)


def interrupted(signum, frame):
    raise InterruptedError()


for event in (signal.SIGHUP, signal.SIGINT, signal.SIGTERM):
    signal.signal(event, interrupted)

try:
    run()
except BaseException:
    # No traceback, JSON, certificate, password or raw exception reaches stderr.
    sys.exit(1)
