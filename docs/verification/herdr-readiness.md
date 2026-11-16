# Herdr discovery and desktop readiness

This is intentional protocol hardening (#72), not behavior-preserving refactoring.
The supported evidence is **hc 0.1.17** and released **herdr-app 0.1.16**.
No desktop, host core, sandbox or installed Herdr state was changed to verify it.

## Primary contracts

- Core v0.1.17, commit `ce87721557176f6cddaac1c7aefbf76929923263`:
  [machine CLI](https://github.com/herdrdev/herdr-core/blob/ce87721557176f6cddaac1c7aefbf76929923263/crates/herdr-core/src/cli/machine.rs#L66-L96)
  passes the `machine.list` object through as JSON, with a `machines` array;
  machine identity is `name`, compared exactly, not target/host or a substring.
  Empty `machines` is genuine absence; failed discovery or invalid rows are not.
- The [official release manifest](https://releases.herdr.dev/app/latest.json)
  identified app 0.1.16 when evidence was collected on 2026-11-16. Its tag pins
  commit `034f8b85f7e6bb71f8ff55cb4a80c548b2578c0f`.
  [Status construction](https://github.com/herdrdev/herdr-app/blob/034f8b85f7e6bb71f8ff55cb4a80c548b2578c0f/src/window.rs#L3939-L3964)
  always returns an object with `errors: string[]`. A valid responsive status
  requires that shape and no singular `error`; unrelated display fields are not
  required here. Lines 3941–3946 collect settings/keys/layout/programs `.error`
  files and mod/layout diagnostics: configuration cleanliness is **not** desktop
  readiness. A nonempty well-formed array does not block an existing window or
  kill a newly started responsive desktop. The launcher warns through its existing
  console without echoing external diagnostic text (which may contain secrets or
  terminal controls), and points the user to `herdr-app status` for local inspection.
- The released [control client](https://github.com/herdrdev/herdr-app/blob/034f8b85f7e6bb71f8ff55cb4a80c548b2578c0f/src/control.rs#L73-L126)
  reports a connection failure as exit 2 with stderr beginning
  `herdr-app: no Herdr window at `. Only that result, with empty stdout, enables
  a bare launch. Other failures, malformed status and singular control errors stop
  with inspection/recovery guidance; they do not justify another app launch.
- Released [main](https://github.com/herdrdev/herdr-app/blob/034f8b85f7e6bb71f8ff55cb4a80c548b2578c0f/src/main.rs#L101-L167)
  raises an existing window and exits, but a fresh bare launch enters a long-lived
  event loop. There is no invented short-lived start command or detached CLI flag.

## Startup ownership

Normal captured `IProcessRunner.RunAsync` still waits for exit. The necessary
additional operation, `RunUntilReadyAsync`, starts a desktop and retains ownership
until a caller-supplied readiness check completes. It observes premature nonzero
exit, cancels and joins its readiness probe, and returns that startup failure.
On failed readiness or caller cancellation it makes the same bounded best-effort
process-tree cleanup as ordinary process cancellation. It never signals a process
it did not start. No automatic app/core restart is implemented.

Only explicitly inherited normal console streams with no supplied stdin are
allowed: a healthy long-lived desktop must not retain CLI-owned capture pipes,
background drain tasks or sensitive-stream exceptions. Once readiness succeeds,
the process handle and exit observer are disposed without waiting for or killing
the desktop. The desktop then belongs to the user. This does not promise a new
OS session or isolation from terminal signals. Captured requests, redaction,
sensitive output suppression, UTF-8 input and ordinary cancellation are unchanged.

The launcher bounds the **entire initial status/start/readiness exchange to 45
seconds**, including individual status calls (the released client can wait 35
seconds). It retries only confirmed missing-window status, with a 250 ms delay;
malformed responses and other failures are immediate failures. Successful bare
process creation alone is never readiness. Startup failure is checked before
`space.new`. Initial sandbox HTTPS gating still precedes every Herdr operation;
the actual sandbox identity, runtime workspace and JSON argv remain authoritative.

Synthetic launcher tests substitute only `IProcessRunner`/`ISbxClient`; controlled
desktop-child tests execute local shell processes with explicit teardown. Existing
Run, legacy, stream-security and process regressions are executed separately. No
live Herdr, display, sbx, Docker, trust, registry or secret operations are claimed.
