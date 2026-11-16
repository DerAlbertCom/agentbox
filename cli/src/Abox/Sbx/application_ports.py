"""Private sbx receiver. One detached, sandbox-owned TCP relay per app port."""

import errno
import fcntl
import json
import os
import secrets
import socket
import stat
import subprocess
import sys
import tempfile
import threading
import time


def fail(message):
    raise RuntimeError(message)


def owned(info, kind):
    if (not kind(info.st_mode) or info.st_uid != os.getuid()
            or info.st_mode & 0o077 or (kind == stat.S_ISREG and info.st_nlink != 1)):
        fail("Unsafe relay state ownership, permissions or file type; inspect ~/.abox-ports-v1 before retrying.")


def read_port(path):
    try:
        fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    except FileNotFoundError:
        return None
    with os.fdopen(fd) as stream:
        owned(os.fstat(stream.fileno()), stat.S_ISREG)
        text = stream.read(16)
        if not text.isascii() or not text.isdecimal() or not 55536 <= int(text) <= 65535:
            fail("Invalid saved relay port; inspect ~/.abox-ports-v1 before retrying.")
        return int(text)


def save_port(path, port):
    fd, temporary = tempfile.mkstemp(dir=os.path.dirname(path))
    try:
        with os.fdopen(fd, "w") as stream:
            stream.write(str(port))
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def status(path, application_port):
    try:
        owned(os.lstat(path), stat.S_ISSOCK)
    except FileNotFoundError:
        return None
    with socket.socket(socket.AF_UNIX) as control:
        control.settimeout(3)
        try:
            control.connect(path)
        except OSError as error:
            if error.errno in (errno.ENOENT, errno.ECONNREFUSED):
                return None
            raise
        data = control.makefile("rb").readline(512)
    reply = json.loads(data)
    if (set(reply) != {"application_port", "relay_port", "pid"}
            or reply["application_port"] != application_port
            or type(reply["relay_port"]) is not int
            or type(reply["pid"]) is not int or reply["pid"] <= 0
            or not 55536 <= reply["relay_port"] <= 65535):
        fail("Unrecognized relay control response; existing listeners were not replaced.")
    return reply["relay_port"]


def pump(source, target):
    try:
        while True:
            data = source.recv(65536)
            if not data:
                target.shutdown(socket.SHUT_WR)
                return
            target.sendall(data)
    except OSError:
        for connection in (source, target):
            try:
                connection.shutdown(socket.SHUT_RDWR)
            except OSError:
                pass


def forward(client, application_port, slots):
    try:
        with client:
            # Explicit loopback addresses avoid DNS; the ingress address also supports
            # apps bound only to the sandbox interface, without changing their binds.
            addresses = ["127.0.0.1", "::1", client.getsockname()[0]]
            target = None
            for address in dict.fromkeys(addresses):
                try:
                    target = socket.create_connection((address, application_port), timeout=3)
                    break
                except OSError:
                    pass
            if target is None:
                return
            with target:
                target.settimeout(None)
                outbound = threading.Thread(target=pump, args=(client, target), daemon=True)
                outbound.start()
                pump(target, client)
                outbound.join()
    finally:
        slots.release()


def serve(application_port, relay_port, control_fd, listener_fd, ready_fd):
    control = socket.socket(fileno=control_fd)
    listener = socket.socket(fileno=listener_fd)
    ready = socket.socket(fileno=ready_fd)
    slots = threading.BoundedSemaphore(128)

    def accept_connections():
        while True:
            client, _ = listener.accept()
            if not slots.acquire(blocking=False):
                client.close()
                continue
            threading.Thread(target=forward, args=(client, application_port, slots), daemon=True).start()

    threading.Thread(target=accept_connections, daemon=True).start()
    ready.sendall(b"ready")
    ready.close()
    response = (json.dumps({"application_port": application_port, "relay_port": relay_port, "pid": os.getpid()}) + "\n").encode()
    while True:
        connection, _ = control.accept()
        with connection:
            connection.settimeout(3)
            try:
                connection.sendall(response)
            except OSError:
                pass


def start(root, application_port, relay_port, excluded):
    path = os.path.join(root, str(application_port) + ".sock")
    running_port = status(path, application_port)
    if running_port is not None:
        if relay_port is not None and running_port != relay_port:
            fail("Relay state disagrees with its running listener; nothing was replaced.")
        return running_port
    if os.path.lexists(path):
        owned(os.lstat(path), stat.S_ISSOCK)
        os.unlink(path)  # Only an owned, confirmed-dead control socket, under the lock.
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as listener:
        listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        if relay_port is not None:
            try:
                listener.bind(("0.0.0.0", relay_port))
            except OSError:
                fail("Saved sandbox relay port %s is occupied; free that listener and retry. No process was killed." % relay_port)
        else:
            for _ in range(128):
                candidate = 55536 + secrets.randbelow(10000)
                if candidate in excluded:
                    continue
                try:
                    listener.bind(("0.0.0.0", candidate))
                    relay_port = candidate
                    break
                except OSError as error:
                    if error.errno != errno.EADDRINUSE:
                        raise
            if relay_port is None:
                fail("No free sandbox relay port in 55536..65535; free a port and retry.")
        listener.listen(128)
        with socket.socket(socket.AF_UNIX) as control:
            control.bind(path)
            os.chmod(path, 0o600)
            control.listen(8)
            parent, child = socket.socketpair()
            with parent, child:
                process = subprocess.Popen(
                    [sys.executable, "-I", "-c", SOURCE, "serve", str(application_port), str(relay_port),
                     str(control.fileno()), str(listener.fileno()), str(child.fileno())],
                    pass_fds=(control.fileno(), listener.fileno(), child.fileno()),
                    start_new_session=True, close_fds=True,
                    stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
                child.close()
                parent.settimeout(10)
                try:
                    if parent.makefile("rb").read(5) != b"ready":
                        fail("Detached relay failed readiness; retry after checking sandbox Python.")
                except BaseException:
                    process.terminate()
                    try:
                        process.wait(timeout=3)
                    except subprocess.TimeoutExpired:
                        process.kill()
                        process.wait()
                    raise
    return relay_port


def ensure(ports, reserved):
    if sys.version_info < (3, 8) or os.getuid() == 0:
        fail("Application ports require Python 3.8+ and a non-root sandbox application user; nothing is installed automatically.")
    home = os.environ.get("HOME", "")
    if not os.path.isabs(home):
        fail("Application-port relay needs an absolute sandbox HOME.")
    info = os.lstat(home)
    if not stat.S_ISDIR(info.st_mode) or info.st_uid != os.getuid() or info.st_mode & 0o022:
        fail("Sandbox HOME must be an owned directory, not writable by other users.")
    root = os.path.join(home, ".abox-ports-v1")
    try:
        os.mkdir(root, 0o700)
    except FileExistsError:
        pass
    owned(os.lstat(root), stat.S_ISDIR)
    fd = os.open(os.path.join(root, "lock"), os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW | os.O_NONBLOCK, 0o600)
    with os.fdopen(fd, "w") as lock:
        owned(os.fstat(lock.fileno()), stat.S_ISREG)
        deadline = time.monotonic() + 10
        while True:
            try:
                fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
                break
            except BlockingIOError:
                if time.monotonic() >= deadline:
                    fail("Another application-port operation holds the sandbox lock; retry shortly.")
                time.sleep(0.1)
        result = []
        for port in ports:
            path = os.path.join(root, str(port) + ".port")
            relay_port = start(root, port, read_port(path), set(ports) | reserved)
            if relay_port in ports:
                fail("A saved relay port conflicts with a requested application port; nothing was replaced.")
            save_port(path, relay_port)
            result.append({"application_port": port, "relay_port": relay_port})
        print(json.dumps(result))


try:
    if sys.argv[1:2] == ["serve"]:
        serve(*map(int, sys.argv[2:]))
    else:
        arguments = sys.argv[1:]
        separator = arguments.index("--reserved") if "--reserved" in arguments else len(arguments)
        ports = sorted(set(map(int, arguments[:separator])))
        reserved = set(map(int, arguments[separator + 1:]))
        if not ports or any(not 1 <= port <= 65535 for port in set(ports) | reserved):
            fail("Invalid application ports.")
        ensure(ports, reserved)
except Exception as error:
    print("Application-port relay: " + str(error), file=sys.stderr)
    sys.exit(1)
