import { join } from "node:path";
import { parseArgs } from "node:util";
import type { Output } from "./api";
import { Commands } from "./commands";
import { styleFor } from "./style";

export interface SboxIO {
  readonly env: Readonly<Record<string, string | undefined>>;
  readonly stdout: Output;
  readonly stderr: Output;
  readonly stdin: { readonly isTTY?: boolean };
}

const usage = "Usage: sbox <command> [args...]\n";

/** The sbox core: routes `sbox <command> [args...]` to the command's module in the commands folder. */
export async function sbox(argv: string[], io: SboxIO): Promise<number> {
  const home = io.env.HOME ?? "";
  const commands = new Commands(join(home, ".local/share/sbox/commands"));
  // Options before the command name belong to sbox, everything after it to the command.
  const start = argv.findIndex((arg) => !arg.startsWith("-"));
  const [name, ...args] = start === -1 ? [] : argv.slice(start);
  let help: boolean | undefined;
  try {
    const options = parseArgs({
      args: start === -1 ? argv : argv.slice(0, start),
      options: { help: { type: "boolean", short: "h" } },
    });
    help = options.values.help;
  } catch (error) {
    io.stderr.write(`sbox: ${(error as Error).message}\n\n${usage}`);
    return 2;
  }

  if (help || name === undefined) {
    io.stdout.write(usage + "\n" + (await commands.overview()));
    return 0;
  }

  if (!commands.names().includes(name)) {
    io.stderr.write(`sbox: the command '${name}' is not installed.\n\n${await commands.overview()}`);
    return 1;
  }

  try {
    const command = await commands.load(name);
    const code = await command.run(args, {
      name,
      paths: { home, state: join(home, ".local/state/sbox", name), data: commands.folderOf(name) },
      env: io.env,
      stdout: io.stdout,
      stderr: io.stderr,
      style: styleFor(Boolean(io.stdout.isTTY) && !io.env.NO_COLOR),
      isTTY: Boolean(io.stdin.isTTY && io.stdout.isTTY),
    });
    return code ?? 0;
  } catch (error) {
    io.stderr.write(`sbox ${name}: ${error instanceof Error ? error.message : String(error)}\n`);
    return 1;
  }
}
