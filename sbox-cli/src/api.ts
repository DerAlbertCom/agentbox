// The command API of sbox. Command kits import it type-only, so keep it small and
// change it only in backward-compatible ways.

/** The default export of a command's bundled module, `<commands>/<name>/command.js`. */
export interface Command {
  /** One line, shown by `sbox --help`. */
  readonly summary: string;
  /** Runs the command with the arguments after its name. Returns the exit code, 0 when omitted. */
  run(args: string[], ctx: CommandContext): number | void | Promise<number | void>;
}

export interface CommandContext {
  /** The name the command was called by. */
  readonly name: string;
  readonly paths: CommandPaths;
  readonly env: Readonly<Record<string, string | undefined>>;
  readonly stdout: Output;
  readonly stderr: Output;
  readonly style: Style;
  /** Whether stdin and stdout are both terminals, so the command may prompt. */
  readonly isTTY: boolean;
}

export interface CommandPaths {
  readonly home: string;
  /** The command's state folder, kept when the sandbox is recreated. It may not exist yet. */
  readonly state: string;
  /** The command's installed folder, holding its module and the data its kit ships. */
  readonly data: string;
}

export interface Output {
  write(text: string): unknown;
  readonly isTTY?: boolean;
}

/** Text styling that is a no-op when the output is not a terminal or `NO_COLOR` is set. */
export interface Style {
  readonly enabled: boolean;
  bold(text: string): string;
  dim(text: string): string;
  red(text: string): string;
  green(text: string): string;
  yellow(text: string): string;
  cyan(text: string): string;
}
