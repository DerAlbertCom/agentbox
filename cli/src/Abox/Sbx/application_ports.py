"""Private sbx receiver. One detached, sandbox-owned TCP relay per app port."""

import argparse
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


SAVED_RELAY_CONFLICT = "A saved relay port conflicts with a requested application port; nothing was replaced."


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


def allocate_listener(listener, saved_port, excluded):
    listener.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    if saved_port is not None:
        restore_listener(listener, saved_port)
        return saved_port
    for _ in range(128):
        candidate = 55536 + secrets.randbelow(10000)
        if candidate in excluded:
            continue
        try:
            listener.bind(("0.0.0.0", candidate))
            return candidate
        except OSError as error:
            if error.errno != errno.EADDRINUSE:
                raise
    fail("No free sandbox relay port in 55536..65535; free a port and retry.")


def restore_listener(listener, saved_port):
    try:
        listener.bind(("0.0.0.0", saved_port))
    except OSError:
        fail("Saved sandbox relay port %s is occupied; free that listener and retry. No process was killed." % saved_port)


def start_detached_relay(application_port, relay_port, control, listener):
    parent, child = socket.socketpair()
    with parent, child:
        process = subprocess.Popen(
            [sys.executable, "-I", "-c", SOURCE, "serve", str(application_port), str(relay_port),
             str(control.fileno()), str(listener.fileno()), str(child.fileno())],
            pass_fds=(control.fileno(), listener.fileno(), child.fileno()),
            start_new_session=True, close_fds=True,
            stdin=subprocess.DEVNULL, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        # This scope alone owns failure cleanup, and only for the child it spawned.
        try:
            child.close()
            parent.settimeout(10)
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
        relay_port = allocate_listener(listener, relay_port, excluded)
        listener.listen(128)
        with socket.socket(socket.AF_UNIX) as control:
            control.bind(path)
            os.chmod(path, 0o600)
            control.listen(8)
            start_detached_relay(application_port, relay_port, control, listener)
    return relay_port


def known_relays(root):
    ports = set()
    for name in os.listdir(root):
        if name.endswith((".port", ".sock")):
            stem = name.rsplit(".", 1)[0]
            if not stem.isascii() or not stem.isdecimal() or not 1 <= int(stem) <= 65535:
                fail("Unrecognized relay state filename; inspect ~/.abox-ports-v1 before retrying.")
            ports.add(int(stem))
    result = {}
    for port in sorted(ports):
        saved = read_port(os.path.join(root, str(port) + ".port"))
        running = status(os.path.join(root, str(port) + ".sock"), port)
        if saved is not None and running is not None and saved != running:
            fail("Relay state disagrees with its running listener; nothing was replaced.")
        relay = saved if saved is not None else running
        if relay is not None:
            if relay in result.values():
                fail("Saved relay ports are not unique; inspect ~/.abox-ports-v1 before retrying.")
            result[port] = relay
    return result


def reject_binding_conflicts(ports, saved, offset, bindings):
    if set(ports) & set(saved.values()):
        fail(SAVED_RELAY_CONFLICT)
    if offset is None:
        return
    # Protect even undiscovered apps and cancellation before the .port was saved.
    targets = {relay: application for application, relay in saved.items()}
    for target, host in bindings:
        if target in targets and host != targets[target] + offset:
            fail("Requested offset change conflicts with existing host port %s -> sandbox target %s. Explicitly remove the exact old binding before changing offsets, or retry with the previous offset; no bindings were changed." % (host, target))
        if target in ports and host != target + offset:
            fail("Direct native mapping conflict at host port %s -> sandbox target %s; ownership is unknown. Inspect sandbox ports and, only if you intend to replace it, explicitly remove only that exact binding before retrying; no bindings were changed." % (host, target))


def ensure_owned_relays(root, ports, reserved, saved):
    result = []
    for port in ports:
        relay_port = start(root, port, saved.get(port), set(ports) | reserved | set(saved.values()))
        if relay_port in ports:
            fail(SAVED_RELAY_CONFLICT)
        # Persist only after readiness. A failed save intentionally leaves the live
        # control socket discoverable, so retry can recover without killing a relay.
        save_port(os.path.join(root, str(port) + ".port"), relay_port)
        saved[port] = relay_port
        result.append({"application_port": port, "relay_port": relay_port})
    return result


def ensure(ports, reserved, offset, bindings):
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
        saved = known_relays(root)
        reject_binding_conflicts(ports, saved, offset, bindings)
        print(json.dumps(ensure_owned_relays(root, ports, reserved, saved)))


try:
    if sys.argv[1:2] == ["serve"]:
        serve(*map(int, sys.argv[2:]))
    else:
        parser = argparse.ArgumentParser()
        parser.add_argument("ports", nargs="+", type=int)
        parser.add_argument("--reserved", nargs="*", type=int, default=[])
        parser.add_argument("--offset", type=int)
        parser.add_argument("--bindings", type=json.loads, default=[])
        arguments = parser.parse_args()
        ports = sorted(set(arguments.ports))
        reserved = set(arguments.reserved)
        if not ports or any(not 1 <= port <= 65535 for port in set(ports) | reserved):
            fail("Invalid application ports.")
        if arguments.offset is not None and (not -65534 <= arguments.offset <= 65534 or any(not 1 <= port + arguments.offset <= 65535 for port in ports)):
            fail("Invalid offset or resulting host port; no relay changes were attempted.")
        if (not isinstance(arguments.bindings, list)
                or any(not isinstance(binding, list) or len(binding) != 2
                       or any(type(port) is not int or not 1 <= port <= 65535 for port in binding)
                       for binding in arguments.bindings)):
            fail("Invalid binding inventory; no relay changes were attempted.")
        ensure(ports, reserved, arguments.offset, arguments.bindings)
except Exception as error:
    print("Application-port relay: " + str(error), file=sys.stderr)
    sys.exit(1)
