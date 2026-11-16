"""Public supervisor source; runtime configuration is read from private state only."""

import datetime
import hashlib
import json
import os
import pathlib
import stat
import subprocess
import time

ROOT = "/home/agent/.local/state/abox-story3-host-cert-20261116"
PROJECT = "/home/agent/workspace/Deals/backend/src/WebApi.External/WebApi.External.csproj"
UID = os.getuid()


def private_read(name):
    path = ROOT + "/" + name
    info = os.lstat(path)
    if not stat.S_ISREG(info.st_mode) or info.st_uid != UID or info.st_nlink != 1 or stat.S_IMODE(info.st_mode) != 0o600:
        raise RuntimeError("Private runtime file protection mismatch")
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW)
    with os.fdopen(fd, "rb") as stream:
        return stream.read(2 * 1024 * 1024)


def process(pid):
    fields = pathlib.Path("/proc/%d/stat" % pid).read_text().rsplit(")", 1)[1].split()
    return {"pid": pid, "start_ticks": int(fields[19]), "pgid": int(fields[2]), "ppid": int(fields[1])}


def record(name, value):
    temporary = ROOT + "/." + name + ".tmp"
    fd = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(fd, "wb") as stream:
        stream.write(json.dumps(value, sort_keys=True).encode())
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, ROOT + "/" + name)


try:
    os.umask(0o077)
    info = os.lstat(ROOT)
    if UID != 1000 or not stat.S_ISDIR(info.st_mode) or info.st_uid != UID or stat.S_IMODE(info.st_mode) != 0o700:
        raise RuntimeError("Wrong supervisor identity/runtime directory")
    config = json.loads(private_read("runtime.json"))
    ready = json.loads(private_read("ready.json"))
    if ready != {"version": 1, "pfx_sha256": hashlib.sha256(private_read("host.pfx")).hexdigest()}:
        raise RuntimeError("Runtime certificate digest mismatch")
    if (set(config) != {"ASPNETCORE_Kestrel__Certificates__Default__Path", "ASPNETCORE_Kestrel__Certificates__Default__Password"}
            or config["ASPNETCORE_Kestrel__Certificates__Default__Path"] != ROOT + "/host.pfx"):
        raise RuntimeError("Unexpected runtime configuration")
    if any("kestrel" in key.lower() for key in os.environ):
        raise RuntimeError("Inherited Kestrel configuration requires inspection")
    environment = dict(os.environ)
    environment.update(config)
    logfd = os.open(ROOT + "/app.log", os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(logfd, "wb") as log:
        child = subprocess.Popen(["dotnet", "run", "--project", PROJECT, "--launch-profile", "Web"],
                                 cwd="/home/agent/workspace/Deals/backend", env=environment,
                                 stdin=subprocess.DEVNULL, stdout=log, stderr=subprocess.STDOUT,
                                 start_new_session=True, close_fds=True)
        ownership = {"sandbox": "rwe-ai-sbx-41bc30", "supervisor": process(os.getpid()),
                     "runner": process(child.pid), "profile": "Web", "project": PROJECT,
                     "utc": datetime.datetime.now(datetime.timezone.utc).isoformat()}
        record("ownership.json", ownership)
        while child.poll() is None:
            if "apphost" not in ownership:
                for entry in os.scandir("/proc"):
                    if not entry.name.isdigit():
                        continue
                    try:
                        candidate = process(int(entry.name))
                        if candidate["ppid"] == child.pid and os.path.basename(os.readlink(entry.path + "/exe")) == "Cape.Deals.WebApi.External":
                            ownership["apphost"] = candidate
                            record("ownership.json", ownership)
                            break
                    except (FileNotFoundError, ProcessLookupError, PermissionError):
                        pass
            time.sleep(0.25)
        record("exit.json", {"exit_code": child.returncode, "utc": datetime.datetime.now(datetime.timezone.utc).isoformat()})
except BaseException as error:
    # No exception message, arguments, inherited environment or app output escapes.
    print(json.dumps({"supervisor_failed": type(error).__name__}), flush=True)
    raise SystemExit(1)
