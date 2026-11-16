"""Exercise the receiver's process/TCP interface inside the disposable sandbox."""

import concurrent.futures
import json
import os
import signal
import socket
import struct
import subprocess
import sys
import tempfile
import threading
import unittest


class ReceiverChecks(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix="abox relay ü ")
        self.home = self.temporary.name
        self.root = os.path.join(self.home, ".abox-ports-v1")

    def tearDown(self):
        if os.path.isdir(self.root) and not os.path.islink(self.root):
            for name in os.listdir(self.root):
                if name.endswith(".sock"):
                    self.stop(os.path.join(self.root, name))
        self.temporary.cleanup()

    def stop(self, path):
        try:
            with socket.socket(socket.AF_UNIX) as control:
                control.settimeout(1)
                control.connect(path)
                _, uid, _ = struct.unpack("3i", control.getsockopt(socket.SOL_SOCKET, socket.SO_PEERCRED, 12))
                self.assertEqual(os.getuid(), uid)
                pid = json.loads(control.makefile("rb").readline(512))["pid"]
                os.kill(pid, signal.SIGTERM)
        except (FileNotFoundError, ConnectionRefusedError):
            pass

    def invoke(self, *ports):
        return subprocess.run(
            [sys.executable, "-I", "-c", "import sys; SOURCE=sys.stdin.read(); exec(compile(SOURCE,'<receiver>','exec'))",
             *map(str, ports)], input=RECEIVER, text=True, capture_output=True,
            env={**os.environ, "HOME": self.home}, timeout=20)

    def ensure(self, port):
        result = self.invoke(port)
        self.assertEqual(0, result.returncode, result.stderr)
        mapping, = json.loads(result.stdout)
        self.assertEqual(port, mapping["application_port"])
        self.assertNotEqual(port, mapping["relay_port"])
        return mapping["relay_port"]

    def state(self):
        os.mkdir(self.root, 0o700)

    def reject(self, port=8203):
        result = self.invoke(port)
        self.assertNotEqual(0, result.returncode)
        self.assertEqual("", result.stdout)
        self.assertIn("Application-port relay:", result.stderr)
        return result

    def test_binary_stream_and_half_close_survive_bootstrap_exit(self):
        # Arrange: the target responds only after receiving EOF.
        with socket.create_server(("127.0.0.1", 0)) as application:
            port = application.getsockname()[1]
            payload = bytes(range(256)) * 4096

            def echo():
                client, _ = application.accept()
                with client:
                    data = bytearray()
                    while True:
                        block = client.recv(65536)
                        if not block:
                            break
                        data.extend(block)
                    client.sendall(data[::-1])

            worker = threading.Thread(target=echo, daemon=True)
            worker.start()
            relay = self.ensure(port)
            # Act: a new process and new TCP connection, after ensure exited.
            with socket.create_connection(("127.0.0.1", relay), timeout=10) as client:
                client.sendall(payload)
                client.shutdown(socket.SHUT_WR)
                response = bytearray()
                while True:
                    data = client.recv(65536)
                    if not data:
                        break
                    response.extend(data)
            # Assert
            self.assertEqual(payload[::-1], response)
            worker.join(timeout=3)
            self.assertFalse(worker.is_alive())

    def test_repeat_and_concurrent_invocations_reuse_one_listener(self):
        # Arrange / Act
        relay = self.ensure(8203)
        with concurrent.futures.ThreadPoolExecutor(max_workers=4) as pool:
            repeated = list(pool.map(lambda _: self.ensure(8203), range(4)))
        # Assert
        self.assertEqual([relay] * 4, repeated)
        self.assertEqual(1, len(json.loads(self.invoke(8203, 8203).stdout)))

    def test_offset_change_refuses_old_bindings_including_no_longer_discovered_apps(self):
        # Arrange: one old app is no longer discovered, but its binding remains.
        relay = self.ensure(8203)
        bindings = json.dumps([[relay, 18203], [9000, 19000]])
        # Act: changing offset cannot silently leave the old mapping and append another.
        result = self.invoke(8204, "--offset", 20000, "--bindings", bindings)
        # Assert
        self.assertNotEqual(0, result.returncode)
        self.assertIn("offset change", result.stderr)
        self.assertIn("18203", result.stderr)
        self.assertFalse(os.path.exists(os.path.join(self.root, "8204.port")))
        self.assertEqual(relay, self.ensure(8203))

    def test_offset_change_after_explicit_binding_removal_reuses_the_owned_relay(self):
        # Arrange
        relay = self.ensure(8203)
        # Act: an unrelated native mapping remains, but no binding targets our relay.
        result = self.invoke(8203, "--offset", 20000, "--bindings", "[[9000,19000]]")
        # Assert
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(relay, json.loads(result.stdout)[0]["relay_port"])

    def test_native_mapping_with_another_host_port_is_an_unproven_conflict_not_an_offset_change(self):
        # Arrange: no relay state exists; a direct native binding may belong to another tool.
        bindings = json.dumps([[8203, 18203]])
        # Act
        result = self.invoke(8203, "--offset", 20000, "--bindings", bindings)
        # Assert
        self.assertIn("Direct native mapping conflict", result.stderr)
        self.assertIn("18203 -> sandbox target 8203", result.stderr)
        self.assertIn("ownership is unknown", result.stderr)
        self.assertIn("explicitly remove only that exact binding", result.stderr)
        self.assertNotIn("offset change", result.stderr)
        self.assertNotEqual(0, result.returncode)
        self.assertFalse(os.path.exists(os.path.join(self.root, "8203.port")))

    def test_offset_retry_after_partial_publication_or_lost_acknowledgement_is_safe(self):
        # Arrange: cancellation can occur after listener readiness but before saving its port.
        relay = self.ensure(8203)
        os.unlink(os.path.join(self.root, "8203.port"))
        bindings = json.dumps([[relay, 18203]])
        # Act
        rejected = self.invoke(8204, "--offset", 20000, "--bindings", bindings)
        retry = self.invoke(8203, 8204, "--offset", 10000, "--bindings", bindings)
        # Assert
        self.assertIn("offset change", rejected.stderr)
        self.assertEqual(0, retry.returncode, retry.stderr)
        self.assertEqual(relay, json.loads(retry.stdout)[0]["relay_port"])

    def test_dead_saved_relays_also_protect_existing_bindings_on_offset_changes(self):
        # Arrange
        relay = self.ensure(8203)
        self.stop(os.path.join(self.root, "8203.sock"))
        # Act
        result = self.invoke(8204, "--offset", 0, "--bindings", json.dumps([[relay, 18203]]))
        # Assert
        self.assertIn("offset change", result.stderr)
        self.assertFalse(os.path.exists(os.path.join(self.root, "8204.port")))

    def test_application_wildcard_port_remains_free_before_and_after_setup(self):
        # Arrange
        with socket.create_server(("0.0.0.0", 0)) as application:
            port = application.getsockname()[1]
            # Act / Assert: setup cannot collide with a running wildcard app.
            self.ensure(port)
        # Nor does it prevent starting the app afterwards.
        with socket.create_server(("0.0.0.0", port)):
            self.ensure(port)

    def test_dead_relay_restarts_on_the_same_saved_port(self):
        # Arrange
        relay = self.ensure(8203)
        path = os.path.join(self.root, "8203.sock")
        self.stop(path)
        # Wait for the owned control socket to stop accepting, without killing by a stale PID.
        for _ in range(100):
            with socket.socket(socket.AF_UNIX) as control:
                try:
                    control.connect(path)
                except ConnectionRefusedError:
                    break
            threading.Event().wait(0.01)
        # Act / Assert
        self.assertEqual(relay, self.ensure(8203))

    def test_occupied_saved_port_fails_without_killing_or_replacing_it(self):
        # Arrange
        self.state()
        with socket.socket() as occupied:
            for port in range(55536, 65536):
                try:
                    occupied.bind(("0.0.0.0", port))
                    break
                except OSError:
                    continue
            occupied.listen()
            path = os.path.join(self.root, "8203.port")
            with open(path, "w") as stream:
                stream.write(str(port))
            os.chmod(path, 0o600)
            # Act / Assert
            self.assertIn("occupied", self.reject().stderr)
            self.assertEqual(port, occupied.getsockname()[1])

    def test_existing_mapping_targets_are_not_used_for_new_relays(self):
        # Arrange: every candidate is reserved by runtime mappings, even without listeners.
        reserved = range(55536, 65536)
        # Act
        result = self.invoke(8203, "--reserved", *reserved)
        # Assert
        self.assertNotEqual(0, result.returncode)
        self.assertIn("No free sandbox relay port", result.stderr)

    def test_linked_state_root_is_rejected(self):
        # Arrange
        with tempfile.TemporaryDirectory() as external:
            os.symlink(external, self.root)
            # Act / Assert
            self.reject()
            self.assertEqual([], os.listdir(external))

    def test_state_with_other_user_permissions_is_rejected(self):
        # Arrange
        self.state()
        os.chmod(self.root, 0o750)
        # Act / Assert
        self.reject()

    def test_linked_lock_is_rejected_without_overwriting_target(self):
        # Arrange
        self.state()
        external = os.path.join(self.home, "sentinel")
        with open(external, "w") as stream:
            stream.write("unchanged")
        os.symlink(external, os.path.join(self.root, "lock"))
        # Act / Assert
        self.reject()
        with open(external) as stream:
            self.assertEqual("unchanged", stream.read())

    def test_linked_saved_port_is_rejected(self):
        # Arrange
        self.state()
        os.symlink("missing", os.path.join(self.root, "8203.port"))
        # Act / Assert
        self.reject()

    def test_malformed_saved_port_is_rejected(self):
        # Arrange
        self.state()
        path = os.path.join(self.root, "8203.port")
        with open(path, "w") as stream:
            stream.write("58203\nextra")
        os.chmod(path, 0o600)
        # Act / Assert
        self.reject()

    def test_foreign_file_at_control_path_is_not_removed(self):
        # Arrange
        self.state()
        path = os.path.join(self.root, "8203.sock")
        with open(path, "w") as stream:
            stream.write("unchanged")
        os.chmod(path, 0o600)
        # Act / Assert
        self.reject()
        with open(path) as stream:
            self.assertEqual("unchanged", stream.read())
        os.unlink(path)  # Not a real control socket; avoid the cleanup connection.


unittest.main(argv=["receiver-checks"], verbosity=2)
