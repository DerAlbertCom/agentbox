"""No prompts/model requests: exercise installed local shell tool interfaces only."""

import json
import os
import select
import subprocess
import sys
import time

sys.excepthook = lambda kind, value, trace: sys.stderr.write("Local-tool probe failed (" + kind.__name__ + "); raw diagnostics suppressed.\n")

assert os.environ["SANDBOX_NAME"].startswith("abox-issue46-activation-")
tool, executable = sys.argv[1:]
assert executable.startswith(os.environ["WORKSPACE_DIR"] + "/payload/")
environment = dict(os.environ)
environment.update({"PI_OFFLINE": "1", "PI_TELEMETRY": "0", "CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC": "1", "DISABLE_AUTOUPDATER": "1", "DISABLE_TELEMETRY": "1"})


def call(argv, **kwargs):
    return subprocess.run(argv, env=environment, stdin=subprocess.DEVNULL, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, timeout=45, check=True, **kwargs).stdout


def send(process, value):
    process.stdin.write(json.dumps(value).encode() + b"\n")
    process.stdin.flush()


def response(process, request_id):
    until = time.monotonic() + 45
    while time.monotonic() < until:
        if not select.select([process.stdout], [], [], max(0, until - time.monotonic()))[0]:
            break
        line = process.stdout.readline()
        if not line:
            break
        try:
            value = json.loads(line)
        except ValueError:
            continue
        if value.get("id") == request_id:
            assert "error" not in value
            return value
    raise RuntimeError("tool response missing")


if tool == "hc":
    call(["hc", "exec", executable, "serve"])
elif tool == "opencode":
    created = json.loads(call(["opencode", "api", "post", "/api/session", "--data", json.dumps({"title": "No-model HTTPS verification", "location": {"directory": os.environ["WORKSPACE_DIR"]}})]))
    session = created["data"]["id"]
    call(["opencode", "api", "post", "/api/session/" + session + "/shell", "--data", json.dumps({"command": executable + " serve"})])
elif tool in ("pi", "claude"):
    argv = ["pi", "--mode", "rpc", "--offline", "--no-session", "--no-extensions", "--no-mcp", "--no-skills", "--no-prompt-templates", "--no-context-files", "--no-themes"] if tool == "pi" else ["claude", "mcp", "serve"]
    process = subprocess.Popen(argv, env=environment, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL, bufsize=0)
    try:
        if tool == "pi":
            send(process, {"id": "probe", "type": "bash", "command": executable + " serve"})
            result = response(process, "probe")
            assert result.get("success") is True
        else:
            send(process, {"jsonrpc": "2.0", "id": 1, "method": "initialize", "params": {"protocolVersion": "2024-11-05", "capabilities": {}, "clientInfo": {"name": "abox-local-probe", "version": "1"}}})
            response(process, 1)
            send(process, {"jsonrpc": "2.0", "method": "notifications/initialized"})
            send(process, {"jsonrpc": "2.0", "id": 2, "method": "tools/list"})
            tools = response(process, 2)["result"]["tools"]
            bash = next(item["name"] for item in tools if item["name"].lower() == "bash")
            send(process, {"jsonrpc": "2.0", "id": 3, "method": "tools/call", "params": {"name": bash, "arguments": {"command": executable + " serve", "timeout": 45000}}})
            assert response(process, 3)["result"].get("isError", False) is False
    finally:
        process.terminate()
        try:
            process.wait(5)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait()
else:
    raise RuntimeError("unsupported probe tool")
