# Reproducible synthetic CLI tests

`Abox.Tests` is an xUnit v3 executable targeting .NET 10. Run that executable
directly: a successful `dotnet test` command that only builds/discovers tests is
not execution evidence. Discovery and execution are separate commands below.

## Prerequisites and resource ownership

- Install a compatible .NET SDK and the .NET 10 runtime; allow package restore
  from the configured feeds. The recorded run used SDK 11.0.100 and runtime
  10.0.15 on Linux x64. No package or production configuration changes are needed.
- Unix process/shell tests need `sh`, `bash` and ordinary shell utilities on PATH.
  Windows cases use PowerShell and Git for Windows shell support where indicated
  by their existing platform gates. Filesystem link/permission semantics matter.
- The executable's output directory must be writable. Certificate fixtures create
  uniquely owned `certificate-tests/<guid>` directories there and dispose their
  contents after each test. Production staging still rejects symlink/reparse-point
  ancestors and still enforces owner-only private staging; choose a physical
  output path without linked ancestors. Do not relax those checks to run tests.
- The build copies only the two shipped agent-guidance kit descriptors and the
  synthetic local/published user-secrets policy inventories into `Fixtures/`.
  Keep that directory with the executable when moving output. Tests never search
  output ancestors or the current directory for a checkout. A missing input names
  the missing resource and asks for a rebuild with `Fixtures/` retained.
- Normal runs use synthetic certificates and substituted external services, not
  host certificate export/trust, live secrets, Docker publication or sbx mutation.
  Leave `ABOX_DISPOSABLE_RECEIVER_CHECKS` unset: its opt-in receiver/bootstrap case
  requires its existing dedicated non-root disposable Linux environment, not an
  ordinary developer machine.

## Ordinary output (Bash, from the repository root)

```sh
repo="$PWD"
evidence="$HOME/abox-test-evidence-$(date +%Y%m%d%H%M%S)"
mkdir -p "$evidence"
dotnet build cli/agentic-box.slnx
ordinary="$repo/cli/test/Abox.Tests/bin/Debug/net10.0/Abox.Tests.dll"
dotnet "$ordinary" -noColor -list tests/json -preEnumerateTheories > "$evidence/ordinary-inventory.json"
dotnet "$ordinary" -noColor -result-xml "$evidence/ordinary.xml"
```

`-list tests/json` lists cases and does **not** run them. The second invocation
runs the suite and emits passed/failed/skipped counts plus an XML report. A
non-zero exit is a failure; do not mask it. Use `-class Abox.Tests.<ClassName>`
on the execution command for a focused run.

## Isolated build and unrelated working directory

Continue in the same shell. Choose a fresh writable physical directory outside
the checkout; do not create fixture-discovery links back to the repository.

```sh
artifacts="$evidence/artifacts"
dotnet build "$repo/cli/agentic-box.slnx" --artifacts-path "$artifacts"
isolated="$artifacts/bin/Abox.Tests/debug/Abox.Tests.dll"
cd "$evidence"
dotnet "$isolated" -noColor -list tests/json -preEnumerateTheories > isolated-inventory.json
dotnet "$isolated" -noColor -result-xml isolated.xml
```

Compare inventories as sorted lists (retain duplicates), not just totals:

```sh
python3 - "$evidence" <<'PY'
import json, pathlib, sys, xml.etree.ElementTree as ET
root = pathlib.Path(sys.argv[1])
inventories = [json.loads((root / f"{name}-inventory.json").read_text())
               for name in ("ordinary", "isolated")]
assert sorted(inventories[0]) == sorted(inventories[1])
print("Matching discovered cases:", len(inventories[0]))
for name in ("ordinary", "isolated"):
    assembly = ET.parse(root / f"{name}.xml").getroot().find("assembly")
    print(name, {key: assembly.attrib[key]
                 for key in ("total", "passed", "failed", "skipped", "errors")})
    assert assembly.attrib["failed"] == assembly.attrib["errors"] == "0"
PY
```

## Verification for #50 (2026-11-16)

The existing high-level tests were the regression seam; no cases or platform
skips were removed or added. Before changes, executing the certificate,
user-secrets rewrite and guidance classes from isolated output found 65 cases:
30 passed, 35 failed, zero skipped (checkout lookup/resource failures). After
each resource slice, actual focused executions passed: certificate 31/31,
verification inventory 32/32 and guidance 2/2.

| Layout | Discovered | Passed | Failed | Skipped |
| --- | ---: | ---: | ---: | ---: |
| Ordinary checkout output | 2,078 | 2,041 | 0 | 37 |
| Isolated `/tmp/opencode/spec49/` output, unrelated working directory | 2,078 | 2,041 | 0 | 37 |

Sorted inventories were identical. Temporarily withholding the copied
`opencode-config.yaml` caused precisely its existing guidance case to fail with
the actionable missing-resource error; the other guidance case passed. The file
was restored afterward. No production source/configuration was edited.

The 37 preserved skips were: 24 Windows workspace-path cases, 8 Windows drive-path
cases, 2 Windows locked-archive cases, 1 Windows junction enumeration case,
1 Windows exclusive-lock cleanup case and 1 opt-in disposable Linux bootstrap
case. This is not Windows, Release, live-host, trust-installation or receiver
opt-in verification. Restore used the public NuGet source explicitly because the
environment's unrelated private feed failed vulnerability metadata retrieval:
`-p:RestoreSources=https://api.nuget.org/v3/index.json`. This command-line feed
selection does not disable package auditing or change repository configuration.
