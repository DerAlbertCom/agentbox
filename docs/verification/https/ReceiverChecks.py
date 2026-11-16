"""Executable protocol checks; root only in a new disposable Linux container.

No test reads the host store or any real certificate. All identities are synthetic.
Run with RunReceiverChecks.ps1, not in an existing sandbox or on a host trust store.
"""
import os
import sys

if (sys.platform != 'linux' or os.geteuid() != 0
        or os.environ.get('ABOX_DISPOSABLE_RECEIVER_CHECKS') != '1'
        or not os.path.isfile('/.dockerenv')):
    print('SKIP: requires the explicit disposable Docker runner (Linux root); no host trust test is permitted.')
    sys.exit(0)

import base64
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import fcntl
import pty
import shutil
import signal
import subprocess
import sys
import time
import unittest

SOURCE = Path('development_certificate.py').read_text()
SHELL = Path('development_certificate_exec.sh').read_text()
ROOT = '/tmp/abox-https-1000'
PASSWORD = 'a' * 64
TRUST_ROOT = '/usr/local/share/ca-certificates/abox-development-https-1000'


def user():
    os.setgroups([])
    os.setgid(1000)
    os.setuid(1000)


def encoded(payload):
    return b'ABOX-CERTIFICATE-1\n%d\n%s\n' % (len(payload), hashlib.sha256(payload).hexdigest().encode()) + payload


def frame(pfx=b'synthetic-private-bytes', password=PASSWORD, public='synthetic-public-only'):
    return encoded(json.dumps(dict(version=1, pfx=base64.b64encode(pfx).decode(), password=password, public=public)).encode())


class ReceiverChecks(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        for suffix in ('one', 'two'):
            subprocess.run(['openssl', 'req', '-x509', '-newkey', 'rsa:2048', '-nodes', '-days', '1',
                            '-subj', '/CN=localhost', '-addext', 'subjectAltName=DNS:localhost',
                            '-keyout', '/tmp/abox-key-' + suffix, '-out', '/tmp/abox-public-' + suffix],
                           check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            os.chmod('/tmp/abox-key-' + suffix, 0o644)  # disposable synthetic key, no host material
        cls.public = Path('/tmp/abox-public-one').read_bytes()
        cls.renewed = Path('/tmp/abox-public-two').read_bytes()

    def setUp(self):
        if os.path.islink(ROOT):
            os.unlink(ROOT)
        shutil.rmtree(ROOT, ignore_errors=True)
        shutil.rmtree(TRUST_ROOT, ignore_errors=True)

    def tearDown(self):
        shutil.rmtree(ROOT, ignore_errors=True)

    def receiver(self, mode, payload=None, **kwargs):
        code = 'SOURCE=' + repr(SOURCE) + '\nexec(compile(SOURCE,"<receiver>","exec"))'
        return subprocess.run(['python3', '-I', '-c', code, mode, 'b' * 32],
                              input=payload, capture_output=True, preexec_fn=user, timeout=10, **kwargs)

    def activate(self, pfx=b'synthetic-private-bytes', public=None):
        result = self.receiver('activate', frame(pfx, public=(public or self.public).decode()))
        self.assertEqual(0, result.returncode, result.stderr)

    def launch_argv(self, command):
        # Exact production shell and installed-source digest, no reimplementation.
        return ['sh', '-c', SHELL, 'abox-https', hashlib.sha256(SOURCE.encode()).hexdigest(), *command]

    def launch(self, command, **kwargs):
        return subprocess.run(self.launch_argv(command), capture_output=True,
                              preexec_fn=kwargs.pop('preexec_fn', user), timeout=10, **kwargs)

    def trust(self, public):
        return subprocess.run(['python3', '-I', 'development_certificate_trust.py', '1000'],
                              input=public, capture_output=True, timeout=40)

    def test_binary_stdin_argv_workdir_and_exit_are_preserved(self):
        # Arrange
        self.activate()
        payload = bytes(range(256)) * 1024
        # Act
        result = self.launch(['sh', '-c', 'test "$1" = \'a b;$(literal)"ü\' || exit 9; cat; printf err >&2; exit 37',
                              '_', 'a b;$(literal)"ü'], input=payload, cwd='/tmp')
        # Assert
        self.assertEqual(payload, result.stdout)
        self.assertEqual(b'err', result.stderr)
        self.assertEqual(37, result.returncode)

    def test_no_https_state_does_not_add_a_python_dependency(self):
        # Arrange: provide only the original sh/id prerequisites, not Python.
        tools = '/tmp/abox-no-python-tools'
        os.mkdir(tools)
        os.symlink('/bin/sh', tools + '/sh')
        os.symlink('/usr/bin/id', tools + '/id')
        # Act
        result = self.launch(['/bin/sh', '-c', 'printf unchanged; exit 37'], env={**os.environ, 'PATH': tools})
        # Assert
        self.assertEqual(37, result.returncode, result.stderr)
        self.assertEqual(b'unchanged', result.stdout)
        self.assertEqual(b'', result.stderr)

    def test_environment_and_locale_are_unchanged_except_documented_certificate_settings(self):
        # Arrange: no LC_ALL/LC_CTYPE; CPython would normally coerce the C locale.
        environment = {**os.environ, 'LANG': 'C', 'LC_MESSAGES': 'C', 'USER_VALUE': ' ü a;\n"'}
        for key in ('LC_ALL', 'LC_CTYPE', 'NODE_EXTRA_CA_CERTS'):
            environment.pop(key, None)
        before = self.launch(['env', '-0'], env=environment)
        self.activate()
        # Act
        after = self.launch(['env', '-0'], env=environment)
        # Assert
        parse = lambda data: dict(value.split(b'=', 1) for value in data.split(b'\0') if value)
        expected, actual = parse(before.stdout), parse(after.stdout)
        for key in (b'ASPNETCORE_Kestrel__Certificates__Default__Path', b'ASPNETCORE_Kestrel__Certificates__Default__Password', b'NODE_EXTRA_CA_CERTS'):
            actual.pop(key)
            expected.pop(key, None)
        self.assertEqual(expected, actual)
        self.assertEqual(0, after.returncode, after.stderr)

    def test_tty_survives_the_configuration_reader(self):
        # Arrange
        self.activate()
        master, slave = pty.openpty()
        try:
            # Act
            result = subprocess.run(self.launch_argv(['sh', '-c', 'test -t 0 && test -t 1 && test -t 2']),
                                    stdin=slave, stdout=slave, stderr=slave, preexec_fn=user, timeout=10)
            # Assert
            self.assertEqual(0, result.returncode)
        finally:
            os.close(master)
            os.close(slave)

    def test_pipefail_and_inherited_ignored_signals_are_transparent(self):
        # Arrange
        self.activate()
        # Act: native yes must die of SIGPIPE, not print EPIPE and exit 1.
        result = self.launch(['bash', '-o', 'pipefail', '-c', 'yes | head -n 1 >/dev/null'])
        # Assert
        self.assertEqual(141, result.returncode, result.stderr)
        self.assertEqual(b'', result.stderr)

    def test_stale_agent_does_not_lock_out_save_and_exit_commands(self):
        # Arrange: actual recognizable process, without the certificate settings.
        self.activate()
        agent = subprocess.Popen(['bash', '-c', 'exec -a opencode sleep 30'], preexec_fn=user)
        try:
            time.sleep(.1)
            # Act
            result = self.launch(['sh', '-c', 'printf saved; exit 37'])
            # Assert
            self.assertEqual(37, result.returncode, result.stderr)
            self.assertEqual(b'saved', result.stdout)
            self.assertEqual(42, self.receiver('status').returncode)
            refused = self.launch(['opencode', '--version'])
            self.assertEqual(42, refused.returncode)
            self.assertIn(b'NOT confirmed', refused.stderr)
            # Recovery commands reach the command rather than the stale gate.
            for command in (['hc', 'server', 'stop'], ['hc', 'server', 'start'],
                            ['hc', 'server', 'restart', '--fresh'], ['opencode', 'service', 'stop']):
                # Do not start a real core: shadow installed tools with an inert stub.
                tools = '/tmp/abox-inert-tools'
                os.makedirs(tools, exist_ok=True)
                for name in ('hc', 'opencode'):
                    Path(tools + '/' + name).write_text('#!/bin/sh\nexit 37\n')
                    os.chmod(tools + '/' + name, 0o755)
                result = self.launch(command, env={**os.environ, 'PATH': tools + ':' + os.environ['PATH']})
                self.assertEqual(37, result.returncode, result.stderr)
        finally:
            agent.terminate()
            agent.wait()

    def test_exact_public_trust_is_idempotent_and_preserves_unrelated_certificates(self):
        # Arrange: generate ONLY disposable synthetic material in this container.
        unrelated = '/usr/local/share/ca-certificates/abox-test-unrelated.crt'
        Path(unrelated).write_bytes(self.public)
        before = Path(unrelated).read_bytes()
        # Act: run the exact production root receiver, twice.
        for _ in range(2):
            result = self.trust(self.public)
            self.assertEqual(0, result.returncode, result.stderr)
        # Assert: exact leaf, root-only management, unrelated anchors retained.
        installed = TRUST_ROOT + '/abox-development-https-1000.crt'
        self.assertEqual(self.public, Path(installed).read_bytes())
        self.assertEqual(0, os.stat(installed).st_uid)
        self.assertEqual(0o644, os.stat(installed).st_mode & 0o777)
        self.assertEqual(before, Path(unrelated).read_bytes())
        result = subprocess.run(['openssl', 'verify', '/tmp/abox-public-one'], capture_output=True)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_renewal_bounds_owned_public_files_and_replaces_exact_leaf(self):
        # Arrange
        shutil.rmtree(TRUST_ROOT, ignore_errors=True)
        self.assertEqual(0, self.trust(self.public).returncode)
        # Act
        self.assertEqual(0, self.trust(self.renewed).returncode)
        # Assert
        self.assertEqual(['.lock', 'abox-development-https-1000.crt'], sorted(os.listdir(TRUST_ROOT)))
        self.assertEqual(self.renewed, Path(TRUST_ROOT + '/abox-development-https-1000.crt').read_bytes())

    def test_node_extra_ca_merge_preserves_user_roots_and_does_not_accumulate_old_leaf(self):
        # Arrange
        self.activate()
        extra = '/tmp/abox-user-extra.pem'
        Path(extra).write_bytes(self.renewed)
        environment = {**os.environ, 'NODE_EXTRA_CA_CERTS': extra}
        # Act
        first = self.launch(['printenv', 'NODE_EXTRA_CA_CERTS'], env=environment)
        self.assertEqual(0, first.returncode, first.stderr)
        bundle = first.stdout.decode().strip()
        self.assertEqual(self.renewed + b'\n' + self.public, Path(bundle).read_bytes())
        self.activate(b'renewed-pfx', public=self.renewed)
        inherited = self.launch(['printenv', 'NODE_EXTRA_CA_CERTS'], env={**environment, 'NODE_EXTRA_CA_CERTS': bundle})
        # Assert
        self.assertEqual(0, inherited.returncode, inherited.stderr)
        self.assertEqual(bundle, inherited.stdout.decode().strip())
        self.assertEqual(self.renewed + b'\n' + self.renewed, Path(bundle).read_bytes())
        self.assertNotIn(self.public, Path(bundle).read_bytes())

    def test_node_conflicting_unreadable_extra_ca_fails_without_discarding_it(self):
        # Arrange
        self.activate()
        # Act
        result = self.launch(['true'], env={**os.environ, 'NODE_EXTRA_CA_CERTS': '/missing-user-trust'})
        # Assert
        self.assertEqual(78, result.returncode)
        self.assertIn(b'No trust configuration was discarded', result.stderr)
        # Setup/readiness must not claim Node activation completed on this input.
        status = self.receiver('status', env={**os.environ, 'NODE_EXTRA_CA_CERTS': '/missing-user-trust'})
        self.assertEqual(44, status.returncode)

    def test_framing_damage_never_replaces_active_private_material(self):
        # Arrange
        self.activate()
        valid = frame(b'new')
        payload = json.dumps(dict(version=1, pfx='bmV3', password=PASSWORD, public='public')).encode()
        invalid = [b'', valid[:-1], valid + b'x', valid.replace(b'ABOX-CERTIFICATE-1', b'ABOX-CERTIFICATE-2'),
                   valid.replace(hashlib.sha256(valid.split(b'\n', 3)[3]).hexdigest().encode(), b'0' * 64),
                   encoded(b'{"version":1,"version":1}'), encoded(payload.replace(b'"bmV3"', b'"%%%"')),
                   encoded(payload.replace(b'"version": 1', b'"version": true'))]
        # Act / Assert
        for damaged in invalid:
            result = self.receiver('activate', damaged)
            self.assertNotEqual(0, result.returncode)
            self.assertEqual(b'', result.stdout)
            self.assertEqual(b'synthetic-private-bytes', Path(ROOT + '/active.pfx').read_bytes())

    def test_protected_files_reject_modes_owners_symlinks_and_hardlinks(self):
        # Arrange / Act / Assert
        for name in ('active.pfx', 'config.json', 'bootstrap.py', 'public.pem', '.lock'):
            for damage in ('mode', 'owner', 'symlink', 'hardlink'):
                with self.subTest(name=name, damage=damage):
                    shutil.rmtree(ROOT, ignore_errors=True)
                    self.activate()
                    path = ROOT + '/' + name
                    original = Path(path).read_bytes()
                    if damage == 'mode':
                        os.chmod(path, 0o644)
                    elif damage == 'owner':
                        os.chown(path, 0, 0)
                    elif damage == 'symlink':
                        os.rename(path, path + '.external')
                        os.symlink(path + '.external', path)
                    else:
                        os.link(path, '/tmp/abox-hardlink')
                    try:
                        result = self.launch(['true'])
                        self.assertEqual(78, result.returncode, (name, damage, result.stderr))
                        self.assertNotEqual(0, self.receiver('activate', frame(b'new')).returncode)
                        self.assertEqual(original, Path(path).read_bytes())
                    finally:
                        if os.path.exists('/tmp/abox-hardlink'):
                            os.unlink('/tmp/abox-hardlink')

    def test_runtime_directory_rejects_symlink_wrong_owner_and_mode(self):
        # Arrange / Act / Assert
        for damage in ('symlink', 'owner', 'mode'):
            self.activate()
            if damage == 'symlink':
                os.rename(ROOT, ROOT + '-moved')
                os.symlink(ROOT + '-moved', ROOT)
            elif damage == 'owner':
                os.chown(ROOT, 0, 0)
            else:
                os.chmod(ROOT, 0o755)
            self.assertEqual(78, self.launch(['true']).returncode)
            if os.path.islink(ROOT):
                os.unlink(ROOT)
                shutil.rmtree(ROOT + '-moved')
            else:
                shutil.rmtree(ROOT)

    def test_atomic_refresh_and_concurrent_launches_do_not_race_transfer_files(self):
        # Arrange
        self.activate()
        def renew(_):
            deadline = time.monotonic() + 8
            while time.monotonic() < deadline:
                result = self.receiver('activate', frame(b'new-private-bytes'))
                if result.returncode == 0:
                    return
                time.sleep(.01)
            self.fail('serialized refresh never succeeded')
        def launch(_):
            result = self.launch(['sh', '-c', 'test ${#ASPNETCORE_Kestrel__Certificates__Default__Password} = 64'])
            self.assertEqual(0, result.returncode, result.stderr)
        # Act
        churn = subprocess.Popen(['sh', '-c', 'while :; do /bin/true; done'], preexec_fn=user)
        try:
            with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
                list(pool.map(renew, range(8)))
                list(pool.map(launch, range(120)))
                futures = [pool.submit(renew, 0) for _ in range(4)] + [pool.submit(launch, 0) for _ in range(120)]
                for future in futures:
                    future.result()
        finally:
            churn.terminate()
            churn.wait()
        # Assert
        self.assertEqual(b'new-private-bytes', Path(ROOT + '/active.pfx').read_bytes())
        self.assertFalse(any(name.startswith('.transfer-') for name in os.listdir(ROOT)))

    def test_lock_contention_and_signal_cancellation_are_bounded_not_invalid_configuration(self):
        # Arrange
        self.activate()
        with open(ROOT + '/.lock', 'rb') as lock:
            fcntl.flock(lock, fcntl.LOCK_EX)
            started = time.monotonic()
            # Act: writer refuses contention; launch reader can be canceled while waiting.
            self.assertNotEqual(0, self.receiver('activate', frame(b'new')).returncode)
            process = subprocess.Popen(self.launch_argv(['sleep', '30']), preexec_fn=user,
                                       stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
            time.sleep(.15)
            os.killpg(process.pid, signal.SIGTERM)
            _, stderr = process.communicate(timeout=3)
        # Assert
        self.assertLess(time.monotonic() - started, 3)
        self.assertNotEqual(78, process.returncode)
        self.assertNotIn(b'invalid', stderr)
        self.assertEqual(0, self.launch(['true']).returncode)

    def test_parent_ignored_sigint_is_preserved_and_normal_termination_is_native(self):
        # Arrange
        self.activate()
        def ignored():
            user()
            signal.signal(signal.SIGINT, signal.SIG_IGN)
        process = subprocess.Popen(self.launch_argv(['sleep', '30']), preexec_fn=ignored,
                                   stdout=subprocess.PIPE, stderr=subprocess.PIPE)
        try:
            for _ in range(100):
                if Path('/proc/' + str(process.pid) + '/comm').read_text().strip() == 'sleep':
                    break
                time.sleep(.01)
            # Act / Assert
            process.send_signal(signal.SIGINT)
            time.sleep(.1)
            self.assertIsNone(process.poll())
            process.terminate()
            _, stderr = process.communicate(timeout=3)
            self.assertEqual(-signal.SIGTERM, process.returncode)
            self.assertNotIn(b'invalid', stderr)
        finally:
            if process.poll() is None:
                process.kill()
                process.wait()

    def test_abandoned_transfer_cleanup_is_exact_and_unknown_files_are_preserved(self):
        # Arrange
        self.activate()
        temporary = ROOT + '/.transfer-' + 'b' * 32 + '.tmp'
        Path(temporary).write_bytes(b'old')
        os.chown(temporary, 1000, 1000)
        os.chmod(temporary, 0o600)
        # Act / Assert
        self.assertEqual(0, self.receiver('cleanup').returncode)
        self.assertFalse(os.path.exists(temporary))
        self.activate(b'new')
        unknown = ROOT + '/unrelated'
        Path(unknown).write_bytes(b'preserve')
        self.assertNotEqual(0, self.receiver('activate', frame(b'bad')).returncode)
        self.assertEqual(b'preserve', Path(unknown).read_bytes())
        self.assertEqual(b'new', Path(ROOT + '/active.pfx').read_bytes())

    def test_changed_bootstrap_is_never_executed(self):
        # Arrange
        self.activate()
        Path(ROOT + '/bootstrap.py').write_text('raise RuntimeError("DO NOT EXECUTE")')
        # Act
        result = self.launch(['true'])
        # Assert
        self.assertEqual(78, result.returncode)
        self.assertIn(b'bootstrap missing or changed', result.stderr)
        self.assertNotIn(b'DO NOT EXECUTE', result.stderr)

    def test_uninspectable_same_user_agent_is_not_reported_as_invalid_configuration(self):
        # Arrange: Linux denies environ even to the same user for a nondumpable process.
        self.activate()
        executable = '/tmp/abox-permission-agent/opencode'
        os.makedirs(os.path.dirname(executable), exist_ok=True)
        os.symlink(sys.executable, executable)
        code = 'import ctypes,time; ctypes.CDLL(None).prctl(4,0); print("ready",flush=True); time.sleep(30)'
        agent = subprocess.Popen([executable, '-c', code], preexec_fn=user, stdout=subprocess.PIPE)
        try:
            self.assertEqual(b'ready\n', agent.stdout.readline())
            # Act
            result = self.launch(['sh', '-c', 'printf saved; exit 37'])
            status = self.receiver('status')
            # Assert
            self.assertEqual(37, result.returncode, result.stderr)
            self.assertEqual(b'saved', result.stdout)
            self.assertEqual(43, status.returncode)
            self.assertIn(b'/proc permissions', result.stderr)
            self.assertNotIn(b'invalid', result.stderr)
        finally:
            agent.terminate()
            agent.wait()
            agent.stdout.close()

    def test_node_default_tls_uses_both_host_leaf_and_existing_user_extra_ca(self):
        # Arrange
        self.activate()
        Path('/tmp/abox-node-user.pem').write_bytes(self.renewed)
        server_code = '''import socket,ssl,sys
c=ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER); c.load_cert_chain(sys.argv[1],sys.argv[2])
s=socket.socket(); s.bind(("127.0.0.1",0)); s.listen(); print(s.getsockname()[1],flush=True)
a,_=s.accept()
with c.wrap_socket(a,server_side=True) as t: t.recv(1)
'''
        for suffix in ('one', 'two'):
            server = subprocess.Popen(['python3', '-c', server_code, '/tmp/abox-public-' + suffix, '/tmp/abox-key-' + suffix],
                                      stdout=subprocess.PIPE, stderr=subprocess.PIPE, preexec_fn=user)
            try:
                port = server.stdout.readline().decode().strip()
                self.assertTrue(port.isdigit())
                # Act: no callbacks, rejectUnauthorized overrides, or insecure flags.
                code = 'const t=require("tls").connect({host:"127.0.0.1",port:Number(process.argv[1]),servername:"localhost"},()=>{if(!t.authorized)process.exit(1);t.end();});t.on("error",()=>process.exit(2));'
                result = self.launch(['node', '-e', code, port], env={**os.environ, 'NODE_EXTRA_CA_CERTS': '/tmp/abox-node-user.pem'})
                # Assert
                self.assertEqual(0, result.returncode, result.stderr)
                server.communicate(timeout=5)
                self.assertEqual(0, server.returncode)
            finally:
                if server.poll() is None:
                    server.kill()
                    server.communicate()

    def test_public_trust_update_failure_is_recoverable_without_false_success(self):
        # Arrange: change only this disposable container's root-owned updater.
        updater = Path('/usr/sbin/update-ca-certificates')
        original, mode = updater.read_bytes(), updater.stat().st_mode & 0o777
        try:
            updater.write_text('#!/bin/sh\nexit 19\n')
            # Act / Assert
            self.assertNotEqual(0, self.trust(self.public).returncode)
        finally:
            updater.write_bytes(original)
            updater.chmod(mode)
        # An identical retry must update the store, not shortcut on equal PEM.
        self.assertEqual(0, self.trust(self.public).returncode)
        self.assertEqual(0, subprocess.run(['openssl', 'verify', '/tmp/abox-public-one'], capture_output=True).returncode)

    def test_public_trust_rejects_private_input_and_unsafe_existing_destinations(self):
        # Arrange / Act / Assert
        for public in (Path('/tmp/abox-key-one').read_bytes(), self.public + self.renewed, b'not a certificate'):
            self.assertNotEqual(0, self.trust(public).returncode)
        for damage in ('symlink', 'hardlink', 'mode', 'owner', 'unknown'):
            with self.subTest(damage=damage):
                shutil.rmtree(TRUST_ROOT, ignore_errors=True)
                self.assertEqual(0, self.trust(self.public).returncode)
                path = TRUST_ROOT + '/abox-development-https-1000.crt'
                if damage == 'symlink':
                    os.unlink(path)
                    os.symlink('/tmp/abox-public-one', path)
                elif damage == 'hardlink':
                    os.link(path, '/tmp/abox-root-hardlink')
                elif damage == 'mode':
                    os.chmod(path, 0o666)
                elif damage == 'owner':
                    os.chown(path, 1000, 1000)
                else:
                    Path(TRUST_ROOT + '/unrelated.crt').write_bytes(self.renewed)
                try:
                    self.assertNotEqual(0, self.trust(self.renewed).returncode)
                    self.assertEqual(self.public, Path(path).read_bytes())
                    if damage == 'unknown':
                        self.assertEqual(self.renewed, Path(TRUST_ROOT + '/unrelated.crt').read_bytes())
                finally:
                    if os.path.exists('/tmp/abox-root-hardlink'):
                        os.unlink('/tmp/abox-root-hardlink')

    def test_ignored_sigpipe_is_not_blindly_reset_to_default(self):
        # Arrange
        self.activate()
        def ignored():
            user()
            signal.signal(signal.SIGPIPE, signal.SIG_IGN)
            signal.signal(signal.SIGXFSZ, signal.SIG_IGN)
        # Act: this parent intentionally wants EPIPE, not termination by SIGPIPE.
        result = self.launch(['bash', '-o', 'pipefail', '-c', 'yes | head -n 1 >/dev/null'], preexec_fn=ignored)
        # Assert
        self.assertEqual(1, result.returncode)
        self.assertIn(b'Broken pipe', result.stderr)
        mask = self.launch(['sh', '-c', 'grep SigIgn /proc/$$/status'], preexec_fn=ignored)
        ignored_bits = int(mask.stdout.split()[1], 16)
        self.assertTrue(ignored_bits & (1 << (signal.SIGPIPE - 1)))
        self.assertTrue(ignored_bits & (1 << (signal.SIGXFSZ - 1)))

    def test_native_signal_dispositions_match_the_no_https_path_including_sigxfsz(self):
        # Arrange
        command = ['sh', '-c', 'grep SigIgn /proc/$$/status']
        before = self.launch(command)
        self.activate()
        # Act
        after = self.launch(command)
        # Assert
        self.assertEqual(before.stdout, after.stdout)
        self.assertEqual(0, after.returncode, after.stderr)

    def test_transfer_cancellation_is_signal_exit_not_configuration_error_and_retry_recovers(self):
        # Arrange
        self.activate()
        code = 'SOURCE=' + repr(SOURCE) + '\nexec(compile(SOURCE,"<receiver>","exec"))'
        process = subprocess.Popen(['python3', '-I', '-c', code, 'activate', 'b' * 32],
                                   stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, preexec_fn=user)
        process.stdin.write(frame(b'new')[:-10])
        process.stdin.flush()
        time.sleep(.1)
        # Act
        process.terminate()
        stdout, stderr = process.communicate(timeout=3)
        # Assert
        self.assertEqual(143, process.returncode)
        self.assertEqual(b'', stdout + stderr)
        self.assertEqual(b'synthetic-private-bytes', Path(ROOT + '/active.pfx').read_bytes())
        self.activate(b'after-recovery')
        self.assertEqual(b'after-recovery', Path(ROOT + '/active.pfx').read_bytes())

    def test_concurrent_first_activation_and_retry_leave_one_complete_configuration(self):
        # Arrange / Act
        with concurrent.futures.ThreadPoolExecutor(max_workers=8) as pool:
            results = list(pool.map(lambda _: self.receiver('activate', frame()), range(8)))
        # Assert: lock contention is retryable, never mismatched state or a traceback.
        self.assertTrue(any(result.returncode == 0 for result in results))
        self.assertTrue(all(result.returncode in (0, 1) and not result.stderr for result in results))
        self.activate()
        self.assertEqual(0, self.launch(['true']).returncode)
        self.assertFalse(any(name.startswith('.transfer-') for name in os.listdir(ROOT)))

    def test_node_trust_variant_limit_is_bounded_and_does_not_evict_existing_inputs(self):
        # Arrange
        self.activate()
        paths = []
        for index in range(17):
            path = '/tmp/abox-extra-' + str(index)
            Path(path).write_bytes(('# variant ' + str(index) + '\n').encode() + self.public)
            paths.append(path)
        # Act
        for path in paths[:16]:
            result = self.launch(['true'], env={**os.environ, 'NODE_EXTRA_CA_CERTS': path})
            self.assertEqual(0, result.returncode, result.stderr)
        full = self.launch(['true'], env={**os.environ, 'NODE_EXTRA_CA_CERTS': paths[16]})
        # Assert
        self.assertEqual(78, full.returncode)
        self.assertIn(b'maximum 16', full.stderr)
        self.assertEqual(16, sum(name.endswith('.source') for name in os.listdir(ROOT)))
        self.assertEqual(0, self.launch(['true'], env={**os.environ, 'NODE_EXTRA_CA_CERTS': paths[0]}).returncode)

    def test_public_trust_directory_is_strictly_root_managed(self):
        # Arrange / Act / Assert
        for damage in ('owner', 'mode', 'symlink'):
            shutil.rmtree(TRUST_ROOT, ignore_errors=True)
            self.assertEqual(0, self.trust(self.public).returncode)
            if damage == 'owner':
                os.chown(TRUST_ROOT, 1000, 1000)
            elif damage == 'mode':
                os.chmod(TRUST_ROOT, 0o777)
            else:
                os.rename(TRUST_ROOT, TRUST_ROOT + '-moved')
                os.symlink(TRUST_ROOT + '-moved', TRUST_ROOT)
            try:
                self.assertNotEqual(0, self.trust(self.renewed).returncode)
                self.assertEqual(self.public, Path(TRUST_ROOT + '/abox-development-https-1000.crt').read_bytes())
            finally:
                if os.path.islink(TRUST_ROOT):
                    os.unlink(TRUST_ROOT)
                    shutil.rmtree(TRUST_ROOT + '-moved')


if __name__ == '__main__':
    if sys.platform != 'linux' or os.geteuid() != 0:
        print('SKIP: requires root in a fresh disposable Linux container; never run on host trust.')
        sys.exit(0)
    if sys.argv[1:] == ['--mount-rejection-only']:
        ReceiverChecks.setUpClass()
        check = ReceiverChecks()
        assert check.receiver('prepare').returncode != 0, 'mounted /tmp must be rejected'
        assert check.trust(ReceiverChecks.public).returncode != 0, 'mounted trust destination must be rejected'
        print('PASS: actual mounted runtime and public-trust destinations rejected')
        sys.exit(0)
    unittest.main(verbosity=2)
