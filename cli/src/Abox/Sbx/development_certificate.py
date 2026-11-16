"""Private runtime certificate transfer and argv-preserving launch bootstrap."""

import base64
import fcntl
import hashlib
import json
import os
import re
import secrets
import signal
import stat
import sys


def require(condition):
    if not condition:
        raise ValueError("unsafe certificate staging")


def ordinary_file(info, uid):
    require(stat.S_ISREG(info.st_mode) and info.st_uid == uid)
    require(stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1)


def unique_object(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result)
        result[key] = value
    return result


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
    data = json.loads(payload, object_pairs_hook=unique_object)
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
        require(name in ("active.json", "config.json", "active.pfx", ".lock") or re.fullmatch(r"\.transfer-[0-9a-f]{32}\.tmp", name) is not None)
        ordinary_file(os.stat(name, dir_fd=root, follow_symlinks=False), uid)


def read_config(root, uid):
    fd = os.open("config.json", os.O_RDONLY | os.O_NOFOLLOW, dir_fd=root)
    with os.fdopen(fd, "rb") as source:
        info = os.fstat(source.fileno())
        ordinary_file(info, uid)
        require(info.st_size <= 4096)
        config = json.loads(source.read(4097), object_pairs_hook=unique_object)
    require(isinstance(config, dict) and set(config) == {"path", "password"})
    require(config["path"] == "/tmp/abox-https-" + str(uid) + "/active.pfx")
    require(isinstance(config["password"], str) and re.fullmatch(r"[0-9a-f]{64}", config["password"]) is not None)
    return config


def settings(config):
    return {"ASPNETCORE_Kestrel__Certificates__Default__Path": config["path"],
            "ASPNETCORE_Kestrel__Certificates__Default__Password": config["password"]}


def parents_current(config, uid):
    expected = settings(config)
    for pid in os.listdir("/proc"):
        if not pid.isdigit() or int(pid) == os.getpid():
            continue
        process = "/proc/" + pid
        try:
            if os.stat(process).st_uid != uid:
                continue
            with open(process + "/cmdline", "rb") as source:
                args = [os.fsdecode(arg) for arg in source.read(65536).split(b"\0") if arg]
            # Inspect executable/entrypoint only, never arbitrary prompt arguments.
            names = [os.path.basename(arg) for arg in args[:2]]
            # Some Linux tool packages retain the compiled executable's .exe suffix.
            names = [name[:-4] if name.endswith(".exe") else name for name in names]
            known = ("hc", "herdr-core", "copilot", "opencode", "pi", "claude")
            agent = bool(names) and names[0] in known
            if len(args) > 1 and os.path.basename(args[0]) in ("node", "bun"):
                agent = names[1] in known or any(part in args[1].split("/") for part in ("claude-code", "copilot", "pi-coding-agent", "opencode-ai"))
            if not agent:
                continue
            with open(process + "/environ", "rb") as source:
                environment = dict(os.fsdecode(value).split("=", 1) for value in source.read(1024 * 1024).split(b"\0") if b"=" in value)
            if any(environment.get(key) != value for key, value in expected.items()):
                return False
        except FileNotFoundError:
            continue
    return True


def bootstrap(uid):
    require(len(sys.argv) >= 3)
    root = open_root(uid, False)
    require(root is not None)
    try:
        check_contents(root, uid)
        config = read_config(root, uid)
        ordinary_file(os.stat("active.pfx", dir_fd=root, follow_symlinks=False), uid)
        # Recovery is explicit and opt-in; never restart a user's core ourselves.
        recovery = sys.argv[2:] in (["hc", "server", "restart", "--fresh"], ["hc", "server", "stop"], ["hc", "server", "start"], ["opencode", "service", "stop"])
        if not recovery and not parents_current(config, uid):
            sys.stderr.write("abox HTTPS: stale Herdr/agent parent; exit old agents. Save work, then explicitly initialize hc with 'abox run shell -- hc server restart --fresh' if supported, otherwise deliberately stop and start hc via abox. No restart was performed.\n")
            return 42
        environment = dict(os.environ)
        environment.update(settings(config))
    finally:
        os.close(root)
    try:
        os.execvpe(sys.argv[2], sys.argv[2:], environment)
    except FileNotFoundError:
        sys.stderr.write("abox: command not found.\n")
        return 127
    except OSError:
        sys.stderr.write("abox: command could not be executed.\n")
        return 126


def run():
    require(sys.platform == "linux" and sys.version_info >= (3, 8))
    uid = os.geteuid()
    require(uid != 0)
    if len(sys.argv) >= 2 and sys.argv[1] == "exec":
        return bootstrap(uid)
    require(len(sys.argv) == 3)
    mode, transfer = sys.argv[1:]
    require(mode in ("stage", "activate", "prepare", "status", "cleanup") and re.fullmatch(r"[0-9a-f]{32}", transfer) is not None)
    payload = read_payload() if mode in ("stage", "activate") else None
    os.umask(0o077)
    root = open_root(uid, mode in ("stage", "activate"))
    if root is None:
        require(mode in ("prepare", "cleanup"))
        if mode == "prepare":
            print(secrets.token_hex(32))
        return
    temporary = ".transfer-" + transfer + ".tmp"
    try:
        lock = os.open(".lock", os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600, dir_fd=root)
        try:
            ordinary_file(os.fstat(lock), uid)
            # Do not let a dead/malicious holder hang cancellation recovery.
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            check_contents(root, uid)
            if mode == "prepare":
                print(read_config(root, uid)["password"] if "config.json" in os.listdir(root) else secrets.token_hex(32))
                return
            if mode == "status":
                config = read_config(root, uid)
                ordinary_file(os.stat("active.pfx", dir_fd=root, follow_symlinks=False), uid)
                return 0 if parents_current(config, uid) else 42
            if mode == "cleanup":
                if temporary in os.listdir(root):
                    os.unlink(temporary, dir_fd=root)
                return
            # Serialized writers can safely remove only validated, abandoned abox
            # staging files. The current writer has not created its file yet.
            for name in os.listdir(root):
                if name.startswith(".transfer-"):
                    os.unlink(name, dir_fd=root)
            target = "active.json"
            if mode == "activate":
                data = json.loads(payload)
                require(re.fullmatch(r"[0-9a-f]{64}", data["password"]) is not None)
                config = {"path": "/tmp/abox-https-" + str(uid) + "/active.pfx", "password": data["password"]}
                if "config.json" in os.listdir(root):
                    require(read_config(root, uid) == config)
                else:
                    # The immutable pair is committed first. If interrupted before
                    # the first PFX, launches fail closed and a retry reuses it.
                    output = os.open(temporary, os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600, dir_fd=root)
                    with os.fdopen(output, "wb") as destination:
                        destination.write(json.dumps(config).encode())
                        destination.flush()
                        os.fsync(destination.fileno())
                    os.replace(temporary, "config.json", src_dir_fd=root, dst_dir_fd=root)
                    os.fsync(root)
                payload = base64.b64decode(data["pfx"], validate=True)
                target = "active.pfx"
            output = os.open(temporary, os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600, dir_fd=root)
            try:
                with os.fdopen(output, "wb") as destination:
                    destination.write(payload)
                    destination.flush()
                    os.fsync(destination.fileno())
                check_contents(root, uid)
                # Activation uses a stable password/path. Only unchanged PFX bytes
                # change on refresh; inherited canonical pairs cannot go stale.
                os.replace(temporary, target, src_dir_fd=root, dst_dir_fd=root)
                if mode == "activate" and "active.json" in os.listdir(root):
                    os.unlink("active.json", dir_fd=root)
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
    result = run()
except BaseException:
    # No traceback, JSON, certificate, password or raw exception reaches stderr.
    if len(sys.argv) > 1 and sys.argv[1] == "exec":
        sys.stderr.write("abox HTTPS: protected launch configuration is invalid or incomplete. Check runtime ownership/mounts and retry 'abox ports'. No fallback certificate was selected.\n")
        sys.exit(78)
    sys.exit(1)
else:
    sys.exit(result or 0)
