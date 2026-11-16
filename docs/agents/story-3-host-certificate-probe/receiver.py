"""One-sandbox, manual verification only. No secret is an argument or diagnostic."""

import base64
import fcntl
import hashlib
import io
import json
import os
import pathlib
import re
import select
import signal
import shutil
import socket
import ssl
import stat
import struct
import subprocess
import sys
import time

ROOT = "/home/agent/.local/state/abox-story3-host-cert-20261116"
OLD = "/home/agent/.local/state/abox-story3-live-20261116T103939Z"
PROJECT = "/home/agent/workspace/Deals/backend/src/WebApi.External/WebApi.External.csproj"
UID = os.getuid()


def fail(message):
    raise RuntimeError(message)


def public_record(data, label):
    try:
        return json.loads(data)
    except json.JSONDecodeError as error:
        fail("%s JSON parse failed: %s; position %d; bytes %d" % (label, error.msg, error.pos, len(data)))


def safe_ancestors(path):
    current = "/"
    for part in pathlib.PurePosixPath(path).parts[1:]:
        if part in ("", ".", ".."):
            fail("Invalid runtime path")
        current = os.path.join(current, part)
        if not os.path.lexists(current):
            break
        info = os.lstat(current)
        if not stat.S_ISDIR(info.st_mode) or info.st_uid not in (0, UID):
            fail("Runtime ancestor type/owner mismatch")
        if stat.S_IMODE(info.st_mode) & 0o022:
            fail("Runtime ancestor writable by another principal")


def private_directory(path):
    safe_ancestors(path)
    info = os.lstat(path)
    if info.st_uid != UID or stat.S_IMODE(info.st_mode) != 0o700:
        fail("Private directory protection mismatch")


def private_file(path):
    info = os.lstat(path)
    if (not stat.S_ISREG(info.st_mode) or info.st_uid != UID
            or stat.S_IMODE(info.st_mode) != 0o600 or info.st_nlink != 1):
        fail("Private file protection mismatch")
    return info


def read_private(path):
    private_file(path)
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    try:
        before = os.fstat(fd)
        if (before.st_ino != os.lstat(path).st_ino or before.st_size > 2 * 1024 * 1024
                or not stat.S_ISREG(before.st_mode) or before.st_uid != UID
                or before.st_nlink != 1 or stat.S_IMODE(before.st_mode) != 0o600):
            fail("Private file changed or is oversized")
        with os.fdopen(fd, "rb", closefd=False) as stream:
            return stream.read(2 * 1024 * 1024 + 1)
    finally:
        os.close(fd)


def write_new(path, data):
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    try:
        with os.fdopen(fd, "wb", closefd=False) as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
    finally:
        os.close(fd)
    private_file(path)


def runtime_safety():
    if UID == 0 or os.environ.get("HOME") != "/home/agent":
        fail("Wrong application user/runtime HOME")
    safe_ancestors(ROOT)
    mounts = []
    for line in pathlib.Path("/proc/self/mountinfo").read_text().splitlines():
        raw = line.split(" - ", 1)[0].split()[4]
        mount = re.sub(r"\\([0-7]{3})", lambda m: chr(int(m.group(1), 8)), raw)
        if mount != "/" and (ROOT == mount or ROOT.startswith(mount.rstrip("/") + "/")
                             or mount.startswith(ROOT + "/")):
            fail("Runtime destination intersects a non-root mount")
        if mount.startswith("/home/agent"):
            mounts.append(mount)
    if os.path.lexists(ROOT):
        private_directory(ROOT)
    return mounts


def proc(pid):
    path = "/proc/%d" % pid
    if os.stat(path).st_uid != UID:
        fail("Process owner mismatch")
    fields = pathlib.Path(path + "/stat").read_text().rsplit(")", 1)[1].split()
    if fields[0] == "Z":
        fail("Process is not live")
    return {"pid": pid, "ppid": int(fields[1]), "pgid": int(fields[2]),
            "start_ticks": int(fields[19])}


def old_ownership():
    private_directory(OLD)
    ownership = public_record(read_private(OLD + "/ownership.json"), "Original task ownership record")
    if ownership.get("sandbox") != "rwe-ai-sbx-41bc30" or ownership.get("app_pid") != 43130:
        fail("Original ownership record sandbox/PID mismatch")
    # First inspection emits schema names only; later stopping checks fixed live identities too.
    leader, app = proc(43130), proc(43843)
    if (leader["start_ticks"] != 1378797 or app["start_ticks"] != 1390583
            or leader["pgid"] != 43130 or app["pgid"] != 43130
            or app["ppid"] != 43130
            or os.path.basename(os.readlink("/proc/43130/exe")) != "dotnet"
            or os.path.basename(os.readlink("/proc/43843/exe")) != "Cape.Deals.WebApi.External"):
        fail("Original task process identity mismatch")
    expected = [b"dotnet", b"run", b"--project", PROJECT.encode(), b"--launch-profile", b"Web"]
    command = pathlib.Path("/proc/43130/cmdline").read_bytes().rstrip(b"\0").split(b"\0")
    if len(command) != len(expected) or os.path.basename(command[0]) != expected[0] or command[1:] != expected[1:]:
        fail("Original task command mismatch")
    if int(read_private(OLD + "/app.pid")) != 43130:
        fail("Original task PID record mismatch")
    expected_records = {43130: (1378797, 43129, "dotnet"), 43843: (1390583, 43130, "Cape.Deals.WebApi.External")}
    records = ownership.get("processes")
    if not isinstance(records, list) or len(records) != 2 or {row.get("pid") for row in records} != set(expected_records):
        fail("Original ownership process records mismatch")
    for row in records:
        ticks, parent, executable = expected_records[row["pid"]]
        if (row.get("start_ticks") != ticks or row.get("process_group") != 43130
                or row.get("ppid") != parent or os.path.basename(row.get("executable", "")) != executable):
            fail("Original ownership start-tick/group/executable record mismatch")
    return ownership, leader, app


def listeners(app_pid):
    owned = set()
    for item in os.scandir("/proc/%d/fd" % app_pid):
        try:
            owned.add(os.readlink(item.path))
        except FileNotFoundError:
            pass
    result = []
    for path, family in [("/proc/net/tcp", socket.AF_INET), ("/proc/net/tcp6", socket.AF_INET6)]:
        for line in pathlib.Path(path).read_text().splitlines()[1:]:
            fields = line.split()
            address, port = fields[1].split(":")
            if fields[3] != "0A" or int(port, 16) != 8203:
                continue
            raw = bytes.fromhex(address)
            raw = raw[::-1] if family == socket.AF_INET else b"".join(raw[i:i + 4][::-1] for i in range(0, 16, 4))
            if "socket:[%s]" % fields[9] not in owned:
                fail("Application listener is not task-owned")
            result.append(socket.inet_ntop(family, raw))
    if sorted(result) != sorted(["127.0.0.1", "::1"]):
        fail("Application loopback listener mismatch")
    return sorted(result)


def relay():
    directory = "/home/agent/.abox-ports-v1"
    private_directory(directory)
    for name in ("lock", "8203.port"):
        private_file(directory + "/" + name)
    info = os.lstat(directory + "/8203.sock")
    if not stat.S_ISSOCK(info.st_mode) or info.st_uid != UID or stat.S_IMODE(info.st_mode) != 0o600:
        fail("Relay socket mismatch")
    with socket.socket(socket.AF_UNIX) as control:
        control.settimeout(3)
        control.connect(directory + "/8203.sock")
        reply = public_record(control.makefile("rb").readline(512), "Relay control status")
    if reply != {"pid": 46234, "relay_port": 60719, "application_port": 8203}:
        fail("Existing relay identity mismatch")
    if int(read_private(directory + "/8203.port")) != 60719 or proc(46234)["start_ticks"] != 1509747:
        fail("Existing relay state mismatch")
    return reply


def preflight():
    mounts = runtime_safety()
    if os.path.lexists(ROOT):
        fail("Fresh manual runtime directory already exists")
    ownership, leader, app = old_ownership()
    profile = public_record(pathlib.Path(os.path.dirname(PROJECT) + "/Properties/launchSettings.json").read_bytes(), "Web launch profile")
    if profile["profiles"]["Web"]["applicationUrl"] != "https://localhost:8203":
        fail("Unchanged Web launch profile mismatch")
    if any("kestrel" in key.lower() for key in os.environ) or any(
            "kestrel" in key.lower() for key in profile["profiles"]["Web"].get("environmentVariables", {})):
        fail("Existing inherited/profile Kestrel settings require inspection")
    return {"uid": UID, "runtime_root": ROOT, "mount_destinations": mounts,
            "old_ownership_keys": sorted(ownership), "old_processes": [leader, app],
            "process_record_shapes": [sorted(entry) for entry in ownership["processes"]],
            "available_trust_commands": {name: shutil.which(name) for name in ("update-ca-certificates", "openssl", "sudo")},
            "application_listeners": listeners(43843), "relay": relay(),
            "runtime_destination_safe": True}


def parse_frame(stream):
    if stream.readline(64) != b"ABOX-MANUAL-CERT-1\n":
        fail("Invalid frame")
    length = stream.readline(32).rstrip(b"\n")
    digest = stream.readline(80).rstrip(b"\n")
    if not length.isdigit() or not 1 <= int(length) <= 2 * 1024 * 1024 or not re.fullmatch(b"[0-9a-f]{64}", digest):
        fail("Invalid frame header")
    data = stream.read(int(length) + 1)
    if len(data) != int(length) or hashlib.sha256(data).hexdigest().encode() != digest:
        fail("Corrupt/truncated frame")
    return json.loads(data)


def frame_selftest():
    before = os.path.lexists(ROOT)
    data = b'{"public_test":true}'
    header = b"ABOX-MANUAL-CERT-1\n" + str(len(data)).encode() + b"\n" + hashlib.sha256(data).hexdigest().encode() + b"\n"
    if parse_frame(io.BytesIO(header + data)) != {"public_test": True}:
        fail("Valid frame round-trip failed")
    malformed = [b"WRONG\n", header + data[:-1], header + data + b"x", header + data.replace(b"true", b"fake"),
                 b"ABOX-MANUAL-CERT-1\n2097153\n" + b"0" * 64 + b"\n",
                 b"ABOX-MANUAL-CERT-1\n0\n" + b"0" * 64 + b"\n",
                 b"ABOX-MANUAL-CERT-1\n1\nnot-a-digest\n{}"]
    for frame in malformed:
        try:
            parse_frame(io.BytesIO(frame))
        except RuntimeError:
            continue
        fail("Malformed frame was accepted")
    if os.path.lexists(ROOT) != before:
        fail("Frame tests changed runtime state")
    return {"valid_frame_roundtrip": True, "malformed_frames_rejected": len(malformed), "runtime_state_unchanged": True}


def receive():
    preflight()
    payload = parse_frame(sys.stdin.buffer)
    if set(payload) != {"version", "pfx", "password", "supervisor", "public_der"} or payload["version"] != 1:
        fail("Invalid frame payload")
    pfx = base64.b64decode(payload["pfx"], validate=True)
    if not 1 <= len(pfx) <= 1024 * 1024 or not re.fullmatch(r"[A-Za-z0-9+/=]{44}", payload["password"]):
        fail("Invalid certificate payload")
    public = base64.b64decode(payload["public_der"], validate=True)
    if hashlib.sha1(public).hexdigest().upper() != "8E84EA4F2F4EC8548DAF359CCB1179008251C714":
        fail("Public certificate identity mismatch")
    compile(payload["supervisor"], ROOT + "/supervisor.py", "exec")
    os.umask(0o077)
    os.mkdir(ROOT, 0o700)
    created = []
    lockfd = os.open(ROOT + "/transfer.lock", os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    fcntl.flock(lockfd, fcntl.LOCK_EX)
    try:
        private_directory(ROOT)
        config = {"ASPNETCORE_Kestrel__Certificates__Default__Path": ROOT + "/host.pfx",
                  "ASPNETCORE_Kestrel__Certificates__Default__Password": payload["password"]}
        for name, value in [("host.pfx", pfx), ("runtime.json", json.dumps(config).encode()),
                            ("supervisor.py", payload["supervisor"].encode()), ("host-public.der", public)]:
            created.append(name)
            write_new(ROOT + "/" + name, value)
        created.append("ready.json")
        write_new(ROOT + "/ready.json", json.dumps({"version": 1, "pfx_sha256": hashlib.sha256(pfx).hexdigest()}).encode())
    except BaseException:
        for name in reversed(created):
            path = ROOT + "/" + name
            if os.path.lexists(path):
                private_file(path)
                os.unlink(path)
        raise
    finally:
        os.close(lockfd)
    return {"staged": True, "runtime_root": ROOT, "private_files_mode": "0600", "directory_mode": "0700"}


def inspect_staged():
    runtime_safety()
    private_directory(ROOT)
    expected = {"host.pfx", "runtime.json", "supervisor.py", "host-public.der", "ready.json", "transfer.lock"}
    if set(os.listdir(ROOT)) != expected:
        fail("Unexpected runtime entries before activation")
    ready = json.loads(read_private(ROOT + "/ready.json"))
    if ready != {"version": 1, "pfx_sha256": hashlib.sha256(read_private(ROOT + "/host.pfx")).hexdigest()}:
        fail("Runtime certificate digest mismatch")
    config = json.loads(read_private(ROOT + "/runtime.json"))
    if set(config) != {"ASPNETCORE_Kestrel__Certificates__Default__Path", "ASPNETCORE_Kestrel__Certificates__Default__Password"}:
        fail("Unexpected runtime configuration")
    if config["ASPNETCORE_Kestrel__Certificates__Default__Path"] != ROOT + "/host.pfx":
        fail("Runtime certificate path mismatch")
    for name in expected:
        private_file(ROOT + "/" + name)
    return {"staged_valid": True, "activated": False}


def cleanup_incomplete():
    runtime_safety()
    if not os.path.lexists(ROOT):
        return {"partial_runtime_absent": True}
    private_directory(ROOT)
    entries = set(os.listdir(ROOT))
    if not entries:
        os.rmdir(ROOT)
        return {"empty_task_runtime_removed": True}
    if not entries <= {"host.pfx", "runtime.json", "supervisor.py", "host-public.der", "ready.json", "transfer.lock"}:
        fail("Unexpected runtime entries; cleanup refused")
    for name in entries:
        private_file(ROOT + "/" + name)
    lockfd = os.open(ROOT + "/transfer.lock", os.O_RDONLY | os.O_NOFOLLOW)
    try:
        fcntl.flock(lockfd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        if "ready.json" in entries:
            inspect_staged()
            return {"single_committed_runtime_retained": True, "activated": False}
        for name in entries:
            os.unlink(ROOT + "/" + name)
        os.rmdir(ROOT)
        return {"partial_task_runtime_removed": True}
    finally:
        os.close(lockfd)


def group_members(group):
    members = []
    for item in os.scandir("/proc"):
        if not item.name.isdigit():
            continue
        try:
            fields = pathlib.Path(item.path + "/stat").read_text().rsplit(")", 1)[1].split()
            if int(fields[2]) == group and fields[0] != "Z":
                if os.stat(item.path).st_uid != UID:
                    fail("Process group contains another owner's process")
                members.append(int(item.name))
        except (FileNotFoundError, ProcessLookupError):
            pass
    return sorted(members)


def activate():
    inspect_staged()
    old_ownership()
    listeners(43843)
    relay()
    if any("kestrel" in key.lower() for key in os.environ):
        fail("Inherited Kestrel settings require inspection before restart")
    if group_members(43130) != [43130, 43843] or shutil.which("dotnet") is None:
        fail("Original task group or startup executable mismatch")
    compile(read_private(ROOT + "/supervisor.py"), ROOT + "/supervisor.py", "exec")
    # pidfds bind signals to the just-verified processes, not reusable numeric PIDs.
    leaderfd = os.pidfd_open(43130)
    appfd = os.pidfd_open(43843)
    try:
        old_ownership()
        if group_members(43130) != [43130, 43843]:
            fail("Original task group changed before stop")
        signal.pidfd_send_signal(appfd, signal.SIGTERM)
        if not select.select([appfd], [], [], 20)[0]:
            fail("Original app did not stop gracefully; no forced kill attempted")
        if not select.select([leaderfd], [], [], 5)[0]:
            signal.pidfd_send_signal(leaderfd, signal.SIGTERM)
            if not select.select([leaderfd], [], [], 10)[0]:
                fail("Original runner did not stop; no forced kill attempted")
    finally:
        os.close(appfd)
        os.close(leaderfd)
    if group_members(43130):
        fail("Original task group still has live members")
    fd = os.open(ROOT + "/supervisor.log", os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "wb") as log:
        child = subprocess.Popen([sys.executable, "-I", ROOT + "/supervisor.py"], stdin=subprocess.DEVNULL,
                                 stdout=log, stderr=subprocess.STDOUT, start_new_session=True, close_fds=True)
    for _ in range(40):
        if os.path.exists(ROOT + "/ownership.json"):
            ownership = json.loads(read_private(ROOT + "/ownership.json"))
            if ownership["supervisor"]["pid"] != child.pid:
                fail("New task supervisor record mismatch")
            return {"activation_started": True, "old_task_stopped": [43130, 43843],
                    "supervisor": ownership["supervisor"], "runner": ownership["runner"], "runtime_root": ROOT}
        if child.poll() is not None:
            fail("Private supervisor exited before recording ownership")
        time.sleep(0.25)
    fail("Private supervisor ownership record was not ready")


def status():
    runtime_safety()
    private_directory(ROOT)
    required = {"host.pfx", "runtime.json", "supervisor.py", "host-public.der", "ready.json", "transfer.lock",
                "app.log", "supervisor.log", "ownership.json"}
    entries = set(os.listdir(ROOT))
    if not required <= entries or not entries <= required | {".ownership.json.tmp"}:
        fail("Unexpected or incomplete active runtime entries")
    for name in entries:
        private_file(ROOT + "/" + name)
    ready = json.loads(read_private(ROOT + "/ready.json"))
    if ready != {"version": 1, "pfx_sha256": hashlib.sha256(read_private(ROOT + "/host.pfx")).hexdigest()}:
        fail("Active runtime certificate digest mismatch")
    ownership = json.loads(read_private(ROOT + "/ownership.json"))
    if ownership.get("sandbox") != "rwe-ai-sbx-41bc30" or ownership.get("profile") != "Web" or ownership.get("project") != PROJECT:
        fail("New task ownership record mismatch")
    for key in ("supervisor", "runner", "apphost"):
        if key not in ownership:
            fail("New task apphost is not recorded yet")
        actual = proc(ownership[key]["pid"])
        fields = ("pid", "start_ticks", "pgid") if key == "supervisor" else ("pid", "start_ticks", "pgid", "ppid")
        if any(actual[field] != ownership[key][field] for field in fields):
            fail("New task process start-tick/group/parent mismatch")
    runner, app = ownership["runner"], ownership["apphost"]
    if runner["pgid"] != runner["pid"] or app["pgid"] != runner["pid"] or app["ppid"] != runner["pid"]:
        fail("New task process lineage mismatch")
    if os.path.basename(os.readlink("/proc/%d/exe" % app["pid"])) != "Cape.Deals.WebApi.External":
        fail("New task apphost executable mismatch")
    return {"supervisor": ownership["supervisor"], "runner": runner, "apphost": app,
            "application_listeners": listeners(app["pid"]), "relay": relay(),
            "runtime_root": ROOT, "private_runtime_valid": True}


def check():
    deadline = time.monotonic() + 180
    while True:
        try:
            result = status()
            break
        except RuntimeError:
            if time.monotonic() >= deadline or os.path.exists(ROOT + "/exit.json"):
                raise
            time.sleep(0.5)
    context = ssl.create_default_context()
    with socket.create_connection(("localhost", 8203), timeout=5) as raw:
        with context.wrap_socket(raw, server_hostname="localhost") as secure:
            der = secure.getpeercert(binary_form=True)
            fingerprint = hashlib.sha256(der).hexdigest().upper()
            if fingerprint != "6A53FFA474740955141D2A0162D5D686A5B27851EF4FF62ED71F4EE465CE26FC":
                fail("Sandbox served certificate DER mismatch")
            result["sandbox_default_tls"] = {"authenticated": True, "check_hostname": context.check_hostname,
                                              "verify_mode": int(context.verify_mode), "leaf_der_sha256": fingerprint,
                                              "leaf_sha1": hashlib.sha1(der).hexdigest().upper()}
    response = subprocess.run(["curl", "-q", "--noproxy", "*", "--proto", "=https", "--connect-timeout", "3",
                               "--max-time", "8", "--head", "--silent", "--output", "/dev/null", "--write-out",
                               "%{http_code}", "https://localhost:8203/"], stdin=subprocess.DEVNULL,
                              stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=12)
    if response.returncode != 0 or not re.fullmatch(b"[1-5][0-9]{2}", response.stdout):
        fail("Sandbox default-validation HEAD failed")
    result["sandbox_default_head"] = {"curl": response.returncode, "http": int(response.stdout)}
    return result


try:
    mode = sys.argv[1]
    if mode == "preflight":
        result = preflight()
    elif mode == "selftest":
        result = frame_selftest()
    elif mode == "receive":
        result = receive()
    elif mode == "inspect-staged":
        result = inspect_staged()
    elif mode == "cleanup-incomplete":
        result = cleanup_incomplete()
    elif mode == "activate":
        result = activate()
    elif mode == "status":
        result = status()
    elif mode == "check":
        result = check()
    else:
        fail("Unsupported mode")
    print(json.dumps(result, sort_keys=True))
except BaseException as error:
    print(json.dumps({"failed": type(error).__name__, "phase": sys.argv[1],
                      "check": str(error) if type(error) is RuntimeError else "Private operation failed"}))
    sys.exit(1)
