"""Private runtime certificate transfer and argv-preserving launch bootstrap."""

import base64
from contextlib import contextmanager
from enum import IntEnum
import fcntl
import hashlib
import json
import os
import re
import secrets
import signal
import ssl
import stat
import sys
import time
from typing import NamedTuple, Optional


NODE_SOURCE_LIMIT = 1024 * 1024
MAX_NODE_SOURCES = 16


class DevelopmentCertificateOutcome(IntEnum):
    # Private receiver contract, also named in SbxClient.DevelopmentCertificate.cs.
    StaleParents = 42
    ProcessEnvironmentsUnavailable = 43
    NodeExtraCaIncomplete = 44
    RuntimeBusy = 75
    InvalidConfiguration = 78


class CertificateOperation(NamedTuple):
    mode: str
    transfer: str
    payload: Optional[bytes]


def require(condition):
    if not condition:
        raise ValueError("unsafe certificate staging")


def ordinary_file(info, uid):
    require(stat.S_ISREG(info.st_mode) and info.st_uid == uid)
    require(stat.S_IMODE(info.st_mode) == 0o600 and info.st_nlink == 1)


class RuntimeBusy(Exception):
    pass


class CallerCAInputError(Exception):
    pass


class NodeTrustCapacityError(Exception):
    pass


def acquire_lock(lock, wait=3):
    # Cleanup's short wait fits its separate five-second host budget. Launches
    # retain the original five-second allowance for queued agent-tool bursts.
    deadline = time.monotonic() + wait
    while True:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            return
        except BlockingIOError:
            if time.monotonic() >= deadline:
                raise RuntimeBusy() from None
            time.sleep(.01)


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
    require(isinstance(data, dict) and set(data) in ({"version", "pfx", "password"}, {"version", "pfx", "password", "public"}))
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
        require(name in ("active.json", "config.json", "active.pfx", ".lock", "bootstrap.py", "public.pem") or re.fullmatch(r"(?:\.transfer-[0-9a-f]{32}\.tmp|node-[0-9a-f]{64}\.(?:pem|source))", name) is not None)
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


def is_agent(args):
    names = [os.path.basename(arg) for arg in args[:2]]
    names = [name[:-4] if name.endswith(".exe") else name for name in names]
    known = ("hc", "herdr-core", "copilot", "opencode", "pi", "claude")
    if names and names[0] in known:
        return True
    return len(args) > 1 and names[0] in ("node", "bun") and (names[1] in known or any(part in args[1].split("/") for part in ("claude-code", "copilot", "pi-coding-agent", "opencode-ai")))


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
            if not is_agent(args):
                continue
            with open(process + "/environ", "rb") as source:
                environment = dict(os.fsdecode(value).split("=", 1) for value in source.read(1024 * 1024).split(b"\0") if b"=" in value)
            if any(environment.get(key) != value for key, value in expected.items()):
                return False
            extra = environment.get("NODE_EXTRA_CA_CERTS", "")
            if re.fullmatch(r"/tmp/abox-https-" + str(uid) + r"/node-[0-9a-f]{64}\.pem", extra) is None:
                return False
        except (FileNotFoundError, ProcessLookupError):
            continue
        except PermissionError:
            return None
    return True


def write_atomic(root, name, payload):
    temporary = ".transfer-" + secrets.token_hex(16) + ".tmp"
    output = os.open(temporary, os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600, dir_fd=root)
    try:
        write_and_sync(output, payload)
        os.replace(temporary, name, src_dir_fd=root, dst_dir_fd=root)
        os.fsync(root)
    finally:
        if temporary in os.listdir(root):
            os.unlink(temporary, dir_fd=root)


def read_public_certificate(root, uid):
    public = os.open("public.pem", os.O_RDONLY | os.O_NOFOLLOW, dir_fd=root)
    with os.fdopen(public, "rb") as source:
        ordinary_file(os.fstat(source.fileno()), uid)
        leaf = source.read(16385)
        require(0 < len(leaf) <= 16384)
        ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT).load_verify_locations(cadata=leaf.decode("ascii"))
    return leaf


def read_node_source(path, uid, owned):
    content = b""
    if path:
        # Caller trust may be a system/corporate CA symlink. Follow it only as
        # this unprivileged user; abox-owned sources remain no-follow.
        fd = os.open(path, os.O_RDONLY | os.O_NONBLOCK | (os.O_NOFOLLOW if owned else 0))
        with os.fdopen(fd, "rb") as source:
            info = os.fstat(source.fileno())
            if owned:
                ordinary_file(info, uid)
            require(stat.S_ISREG(info.st_mode) and info.st_size <= NODE_SOURCE_LIMIT)
            content = source.read(NODE_SOURCE_LIMIT + 1)
            require(len(content) <= NODE_SOURCE_LIMIT)
    if content:
        validate_node_certificates(content)
    return content


def validate_node_certificates(content):
    # Node/OpenSSL allow non-ASCII comments and Bag Attributes outside PEM.
    # Validate complete ASCII certificates without normalizing the source bytes.
    blocks = re.findall(rb"-----BEGIN CERTIFICATE-----.*?-----END CERTIFICATE-----", content, re.DOTALL)
    require(blocks and len(blocks) == content.count(b"-----BEGIN CERTIFICATE-----") == content.count(b"-----END CERTIFICATE-----"))
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    for block in blocks:
        context.load_verify_locations(cadata=block.decode("ascii"))


def caller_node_source(path, uid):
    try:
        return read_node_source(path, uid, False)
    except (OSError, ValueError):
        raise CallerCAInputError() from None


def persist_node_bundle(root, uid, content, leaf):
    name = "node-" + hashlib.sha256(content).hexdigest()
    if name + ".source" in os.listdir(root):
        fd = os.open(name + ".source", os.O_RDONLY | os.O_NOFOLLOW, dir_fd=root)
        with os.fdopen(fd, "rb") as source:
            ordinary_file(os.fstat(source.fileno()), uid)
            require(source.read(NODE_SOURCE_LIMIT + 1) == content)
    else:
        # Bound distinct user trust configurations, never evict a running Node's
        # trust file or silently replace a user's independently selected CA roots.
        if sum(entry.endswith(".source") for entry in os.listdir(root)) >= MAX_NODE_SOURCES:
            raise NodeTrustCapacityError()
        write_atomic(root, name + ".source", content)
    write_atomic(root, name + ".pem", content + b"\n" + leaf)
    return name + ".pem"


def node_bundle(root, uid):
    # Protected-state failures must never be treated as recoverable caller input.
    leaf = read_public_certificate(root, uid)
    original = os.environ.get("NODE_EXTRA_CA_CERTS", "")
    prefix = "/tmp/abox-https-" + str(uid) + "/"
    if original.startswith(prefix):
        require(re.fullmatch(r"node-[0-9a-f]{64}\.pem", original[len(prefix):]) is not None)
        path = original[:-4] + ".source"
        content = read_node_source(path, uid, True)
        require(path == prefix + "node-" + hashlib.sha256(content).hexdigest() + ".source")
    else:
        content = caller_node_source(original, uid)
    return persist_node_bundle(root, uid, content, leaf)


@contextmanager
def locked_runtime(uid, create=False, wait=3):
    root = open_root(uid, create)
    if root is None:
        yield None
        return
    try:
        lock = os.open(".lock", os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600, dir_fd=root)
        try:
            ordinary_file(os.fstat(lock), uid)
            acquire_lock(lock, wait)
            check_contents(root, uid)
            yield root
        finally:
            os.close(lock)
    finally:
        os.close(root)


def bootstrap(uid):
    require(len(sys.argv) >= 3)
    with locked_runtime(uid, wait=5) as root:
        require(root is not None)
        return launch_environment(root, uid, sys.argv[2:])


def launch_environment(root, uid, command):
    config = read_config(root, uid)
    ordinary_file(os.stat("active.pfx", dir_fd=root, follow_symlinks=False), uid)
    recovery = command in (["hc", "server", "restart", "--fresh"], ["hc", "server", "stop"], ["hc", "server", "start"], ["opencode", "service", "stop"])
    refusal = parent_launch_refusal(parents_current(config, uid), command, recovery)
    if refusal is not None:
        return refusal
    try:
        node = node_bundle(root, uid)
    except (CallerCAInputError, NodeTrustCapacityError):
        sys.stderr.write("abox HTTPS: Node trust is NOT configured. Check NODE_EXTRA_CA_CERTS (readable regular PEM file, at most 1 MiB; maximum 16 distinct trust inputs). Preserve/fix the caller input or inspect unused owned node pairs, then retry 'abox ports'. Ordinary/recovery commands retain the caller's original Node setting and protected Kestrel configuration.\n")
        if is_agent(command) and not recovery:
            return DevelopmentCertificateOutcome.InvalidConfiguration
        node = "-"
    print(config["password"] + " " + node)


def parent_launch_refusal(current, command, recovery):
    if current is True:
        return None
    sys.stderr.write("abox HTTPS: " + ("stale Herdr/agent parent" if current is False else "cannot inspect existing agent process environment (/proc permissions)") + "; existing agents are NOT confirmed configured. Save/exit and ordinary transport remain available. Explicitly initialize fresh parents through abox; no restart was performed.\n")
    if is_agent(command) and not recovery:
        return DevelopmentCertificateOutcome.StaleParents if current is False else DevelopmentCertificateOutcome.ProcessEnvironmentsUnavailable
    return None


def remove_transfer(root, temporary):
    if temporary in os.listdir(root):
        os.unlink(temporary, dir_fd=root)


def remove_abandoned_transfers(root):
    # Called only by serialized writers after every entry has been validated.
    # The current writer has not created its transfer file yet.
    for name in os.listdir(root):
        if name.startswith(".transfer-"):
            os.unlink(name, dir_fd=root)


def write_transfer(root, temporary, payload):
    output = os.open(temporary, os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600, dir_fd=root)
    write_and_sync(output, payload)


def write_and_sync(output, payload):
    # Private-runtime descriptor ownership only; root public trust stays separate.
    with os.fdopen(output, "wb") as destination:
        destination.write(payload)
        destination.flush()
        os.fsync(destination.fileno())


class CertificateCommit:
    """Commit stages under the caller's validated runtime lock, never outside it."""

    def __init__(self, root, uid):
        self.root = root
        self.uid = uid

    def activate(self, temporary, payload):
        data = json.loads(payload)
        require(re.fullmatch(r"[0-9a-f]{64}", data["password"]) is not None)
        config = {"path": "/tmp/abox-https-" + str(self.uid) + "/active.pfx", "password": data["password"]}
        require(isinstance(data.get("public"), str) and len(data["public"]) <= 16384)
        if "config.json" in os.listdir(self.root):
            require(read_config(self.root, self.uid) == config)
        else:
            # Commit the immutable pair first. Interrupted first activation fails
            # closed; retry must reuse this same path/password before the PFX.
            write_transfer(self.root, temporary, json.dumps(config).encode())
            os.replace(temporary, "config.json", src_dir_fd=self.root, dst_dir_fd=self.root)
            os.fsync(self.root)
        write_atomic(self.root, "public.pem", data["public"].encode("ascii"))
        self.install_bootstrap()
        self.commit(temporary, "active.pfx", base64.b64decode(data["pfx"], validate=True))

    def install_bootstrap(self):
        installed = None
        if "bootstrap.py" in os.listdir(self.root):
            fd = os.open("bootstrap.py", os.O_RDONLY | os.O_NOFOLLOW, dir_fd=self.root)
            with os.fdopen(fd, "rb") as source:
                installed = source.read(131072)
        # Stable code is not replaced on renewal: a launcher can validate its
        # inode before acquiring the setup lock.
        if installed != SOURCE.encode("utf-8"):
            write_atomic(self.root, "bootstrap.py", SOURCE.encode("utf-8"))

    def commit(self, temporary, target, payload):
        # Preserve the final staging-file cleanup even when writing fails.
        output = os.open(temporary, os.O_CREAT | os.O_EXCL | os.O_WRONLY | os.O_NOFOLLOW, 0o600, dir_fd=self.root)
        try:
            write_and_sync(output, payload)
            check_contents(self.root, self.uid)
            # Stable path/password: renewal replaces only the certificate bytes.
            os.replace(temporary, target, src_dir_fd=self.root, dst_dir_fd=self.root)
            if target == "active.pfx" and "active.json" in os.listdir(self.root):
                os.unlink("active.json", dir_fd=self.root)
            os.fsync(self.root)
        finally:
            remove_transfer(self.root, temporary)


def runtime_status(root, uid):
    config = read_config(root, uid)
    ordinary_file(os.stat("active.pfx", dir_fd=root, follow_symlinks=False), uid)
    try:
        node_bundle(root, uid)
    except (CallerCAInputError, NodeTrustCapacityError):
        return DevelopmentCertificateOutcome.NodeExtraCaIncomplete
    current = parents_current(config, uid)
    return 0 if current is True else DevelopmentCertificateOutcome.StaleParents if current is False else DevelopmentCertificateOutcome.ProcessEnvironmentsUnavailable


def dispatch(root, uid, operation):
    if root is None:
        require(operation.mode in ("prepare", "cleanup"))
        if operation.mode == "prepare":
            print(secrets.token_hex(32))
        return
    temporary = ".transfer-" + operation.transfer + ".tmp"
    if operation.mode == "prepare":
        print(read_config(root, uid)["password"] if "config.json" in os.listdir(root) else secrets.token_hex(32))
        return
    if operation.mode == "refresh-bootstrap":
        read_config(root, uid)
        ordinary_file(os.stat("active.pfx", dir_fd=root, follow_symlinks=False), uid)
        read_public_certificate(root, uid)
        write_atomic(root, "bootstrap.py", SOURCE.encode("utf-8"))
        return
    if operation.mode == "status":
        return runtime_status(root, uid)
    if operation.mode == "cleanup":
        remove_transfer(root, temporary)
        return
    remove_abandoned_transfers(root)
    commit = CertificateCommit(root, uid)
    if operation.mode == "activate":
        commit.activate(temporary, operation.payload)
    else:
        commit.commit(temporary, "active.json", operation.payload)


def run():
    require(sys.platform == "linux" and sys.version_info >= (3, 8))
    uid = os.geteuid()
    require(uid != 0)
    os.umask(0o077)
    if len(sys.argv) >= 2 and sys.argv[1] == "environment":
        return bootstrap(uid)
    require(len(sys.argv) == 3)
    mode, transfer = sys.argv[1:]
    require(mode in ("stage", "activate", "prepare", "status", "cleanup", "refresh-bootstrap") and re.fullmatch(r"[0-9a-f]{32}", transfer) is not None)
    payload = read_payload() if mode in ("stage", "activate") else None
    with locked_runtime(uid, create=mode in ("stage", "activate")) as root:
        return dispatch(root, uid, CertificateOperation(mode, transfer, payload))


def interrupted(signum, frame):
    raise SystemExit(128 + signum)


for event in (signal.SIGHUP, signal.SIGINT, signal.SIGTERM):
    if signal.getsignal(event) != signal.SIG_IGN:
        signal.signal(event, interrupted)

try:
    result = run()
except RuntimeBusy:
    sys.stderr.write("abox HTTPS: protected runtime is busy after a bounded lock wait; retry the operation or 'abox ports'. No complete readiness is claimed.\n")
    sys.exit(DevelopmentCertificateOutcome.RuntimeBusy)
except Exception:
    # No traceback, JSON, certificate, password or raw exception reaches stderr.
    if len(sys.argv) > 1 and sys.argv[1] == "environment":
        sys.stderr.write("abox HTTPS: protected launch configuration is invalid or incomplete (including public.pem or owned Node sources). Check runtime ownership, permissions, mounts and file integrity; retry 'abox ports'. No complete readiness is claimed and no trust configuration was discarded.\n")
        sys.exit(DevelopmentCertificateOutcome.InvalidConfiguration)
    sys.exit(1)
else:
    sys.exit(result or 0)
