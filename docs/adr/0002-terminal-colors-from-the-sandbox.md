# Terminal colors are set from inside the sandbox; abox only steers SBOX_TERMINAL_THEME

The host terminal that runs `abox run` kept its own colors, so a themed sandbox was drawn on the host's background and ANSI palette. The terminal now takes the current theme's colors through OSC sequences (OSC 4 for the 16 ANSI colors, OSC 10, 11 and 12 for foreground, background and cursor), which change only the live session, never the host terminal's configuration. The sandbox sends them: the `themes` kit renders a terminal color script like any other app template, a login script runs it and registers an `EXIT` trap that prints the resets (OSC 104, 110, 111, 112), and `sbox theme` reruns it in its own terminal after a switch. Both act only with `SBOX_TERMINAL_THEME=1` and a terminal on stdout. `abox` knows nothing about themes or colors: it passes `SBOX_TERMINAL_THEME=1` to the sandbox exec unless the user turned it off, and does no reset of its own.

## Considered Options

- **`abox` emits the sequences.** Rejected: `abox` would need the palettes, the current theme and its fallback chain, which live in the sandbox and change with `sbox theme`; a switch inside the sandbox could not recolor the terminal at once.
- **Login runs `sbox` to print the colors.** Rejected: it starts Bun on every login, and a broken command would slow down or break the shell. A pre-rendered `sh` script stays fast and fails quietly.
- **A host-side reset by `abox` after the exec ends.** Rejected for now: it would cover dropped connections and crashed sandboxes, but it puts terminal knowledge back into `abox` and resets terminals that were never recolored. Querying and restoring the host's original colors is out of scope as well.
- **Recolor every terminal of the sandbox on a switch.** Rejected: writing to other terminals can garble a full-screen app running there; they pick up the theme at their next login.

## Consequences

- Only terminals whose shell sees the variable are recolored. `shell` runs `bash -l` in the exec that gets it; Herdr's panes are started by `hc` inside the sandbox and do not, until a launcher delivers it.
- A shell that ends without running its `EXIT` trap (a dropped connection, a killed sandbox) leaves the theme's colors in the host terminal, as do terminals that ignore the reset sequences.
- A login before the first render, such as while the startup hook still runs on a fresh sandbox, gets no colors.
- Non-interactive runs (`abox exec`, `abox pipe`, the kit's hooks, scripts without a terminal) never emit the sequences, so none end up in captured output.
