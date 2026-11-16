"""Install only the explicitly authorized public host leaf with Debian's existing tool."""

import base64
import hashlib
import json
import os
import pathlib
import re
import ssl
import stat
import subprocess
import sys

DIRECTORY = "/usr/local/share/ca-certificates"
TARGET = DIRECTORY + "/abox-story3-host-8e84ea4f2f4ec8548daf359ccb1179008251c714.crt"
EXPECTED = "6a53ffa474740955141d2a0162d5d686a5b27851ef4ff62ed71f4ee465ce26fc"


def fail(message):
    raise RuntimeError(message)


def ordinary_root_path(path, directory):
    parts = pathlib.PurePosixPath(path).parts
    current = "/"
    for index, part in enumerate(parts[1:]):
        current = os.path.join(current, part)
        info = os.lstat(current)
        if info.st_uid != 0 or stat.S_IMODE(info.st_mode) & 0o022:
            fail("Trust path owner/permissions mismatch")
        is_directory = directory or index < len(parts) - 2
        if (is_directory and not stat.S_ISDIR(info.st_mode)) or (not is_directory and not stat.S_ISREG(info.st_mode)):
            fail("Trust path is redirected or has an unsuitable type")
        if not is_directory and info.st_nlink != 1:
            fail("Trust executable hard-link mismatch")


def inventory(directory):
    result = {}
    for path in pathlib.Path(directory).rglob("*"):
        if str(path) == TARGET or str(path) == "/etc/ssl/certs/ca-certificates.crt":
            continue
        info = path.lstat()
        if stat.S_ISLNK(info.st_mode):
            result[str(path)] = ("link", os.readlink(path))
        elif stat.S_ISREG(info.st_mode):
            result[str(path)] = ("file", hashlib.sha256(path.read_bytes()).hexdigest())
    return result


try:
    if os.getuid() != 0:
        fail("This narrowly scoped trust operation requires the sandbox root exec user")
    ordinary_root_path(DIRECTORY, True)
    ordinary_root_path("/etc/ssl/certs", True)
    ordinary_root_path("/usr/sbin/update-ca-certificates", False)
    for line in pathlib.Path("/proc/self/mountinfo").read_text().splitlines():
        raw = line.split(" - ", 1)[0].split()[4]
        mount = re.sub(r"\\([0-7]{3})", lambda m: chr(int(m.group(1), 8)), raw)
        for destination in (DIRECTORY, "/etc/ssl/certs"):
            if mount != "/" and (destination == mount or destination.startswith(mount.rstrip("/") + "/")
                                 or mount.startswith(destination + "/")):
                fail("Trust destination intersects a non-root mount")
    if sys.argv[1] == "check":
        result = {"supported_trust_tool": "/usr/sbin/update-ca-certificates", "target_exists": os.path.lexists(TARGET),
                  "target": TARGET, "public_only": True}
    elif sys.argv[1] == "audit":
        ordinary_root_path(TARGET, False)
        pem = pathlib.Path(TARGET).read_text(encoding="ascii")
        der = ssl.PEM_cert_to_DER_cert(pem)
        if hashlib.sha256(der).hexdigest() != EXPECTED:
            fail("Installed public host certificate identity mismatch")
        files = []
        for path in pathlib.Path("/etc/ssl/certs").rglob("*"):
            if path.is_file() and not path.is_symlink():
                files.append(str(path))
        verify = subprocess.run(["/usr/bin/openssl", "verify", "-purpose", "sslserver", "-verify_hostname", "localhost", "/dev/stdin"],
                                input=pem.encode("ascii"), stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=10)
        result = {"installed_public_der_sha256": EXPECTED.upper(), "target": TARGET,
                  "default_openssl_verify_exit": verify.returncode, "regular_system_trust_files": sorted(files)}
    elif sys.argv[1] == "install":
        stream = sys.stdin.buffer
        if stream.readline(64) != b"ABOX-PUBLIC-TRUST-1\n":
            fail("Invalid public trust frame")
        encoded = stream.read(16385)
        if len(encoded) > 16384:
            fail("Oversized public trust frame")
        der = base64.b64decode(encoded, validate=True)
        if hashlib.sha256(der).hexdigest() != EXPECTED:
            fail("Public host certificate identity mismatch")
        pem = ssl.DER_cert_to_PEM_cert(der).encode("ascii")
        before_local = inventory(DIRECTORY)
        before_system = inventory("/etc/ssl/certs")
        added = False
        if os.path.lexists(TARGET):
            ordinary_root_path(TARGET, False)
            if pathlib.Path(TARGET).read_bytes() != pem:
                fail("Trust target already has unrelated content; refusing replacement")
        else:
            fd = os.open(TARGET, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o644)
            with os.fdopen(fd, "wb") as destination:
                destination.write(pem)
                destination.flush()
                os.fsync(destination.fileno())
            os.chmod(TARGET, 0o644, follow_symlinks=False)
            added = True
        updated = subprocess.run(["/usr/sbin/update-ca-certificates"], stdin=subprocess.DEVNULL,
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=60)
        if updated.returncode != 0:
            fail("Supported trust update failed; exact public target retained for recovery")
        if inventory(DIRECTORY) != before_local:
            fail("Unrelated local certificate content changed")
        after_system = inventory("/etc/ssl/certs")
        changed = [path for path, value in before_system.items() if after_system.get(path) != value]
        if changed:
            print(json.dumps({"failed": "PreservationCheck", "changed_existing_public_paths": sorted(changed),
                              "supported_update_exit": updated.returncode, "exact_public_target_retained": True}))
            sys.exit(1)
        result = {"trusted_public_der_sha256": EXPECTED.upper(), "added_public_target": added,
                  "target": TARGET, "unrelated_local_certificates_preserved": True,
                  "existing_system_trust_entries_preserved": True, "host_trust_untouched": True}
    else:
        fail("Unsupported trust operation")
    print(json.dumps(result, sort_keys=True))
except Exception as error:
    print(json.dumps({"failed": type(error).__name__,
                      "check": str(error) if type(error) is RuntimeError else "Private trust-tool diagnostics suppressed"}))
    sys.exit(1)
