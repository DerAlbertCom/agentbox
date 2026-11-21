import { afterEach, beforeEach, describe, expect, test } from "bun:test";
import { chmod, cp, mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { sbox } from "../../sbox-cli/src/sbox";

class Capture {
  constructor(readonly isTTY = false) {}
  text = "";
  write(chunk: string) {
    this.text += chunk;
  }
}

const packageFolder = join(import.meta.dir, "..");

let home: string;

beforeEach(async () => {
  home = await mkdtemp(join(tmpdir(), "sbox-theme-picker-"));
  await install();
});

afterEach(async () => {
  await rm(home, { recursive: true, force: true });
});

const data = () => join(home, ".local/share/sbox/commands/theme");
const state = () => join(home, ".local/state/sbox/theme");
const bin = () => join(home, "bin");
const fzfLog = () => join(home, "fzf.log");

/** Installs the theme command with the palettes and app definitions of this package. */
async function install() {
  await mkdir(data(), { recursive: true });
  await writeFile(
    join(data(), "command.js"),
    `export { default } from ${JSON.stringify(join(packageFolder, "src/command.ts"))};\n`,
  );
  await cp(join(packageFolder, "themes"), join(data(), "themes"), { recursive: true });
  await cp(join(packageFolder, "apps"), join(data(), "apps"), { recursive: true });
}

async function save(theme: string) {
  await mkdir(state(), { recursive: true });
  await writeFile(join(state(), "current"), theme + "\n");
}

/** Puts a fake `fzf` on PATH that logs its arguments and input, then prints `selection` or cancels. */
async function fakeFzf(selection: string | null) {
  await mkdir(bin(), { recursive: true });
  const answer = selection === null ? "exit 130" : `echo ${selection}`;
  const script = `#!/bin/sh
for arg in "$@"; do echo "arg: $arg"; done >> "${fzfLog()}"
while read -r line; do echo "item: $line"; done >> "${fzfLog()}"
${answer}
`;
  await writeFile(join(bin(), "fzf"), script);
  await chmod(join(bin(), "fzf"), 0o755);
}

async function fzfCalls(): Promise<string[] | null> {
  try {
    return (await readFile(fzfLog(), "utf8")).trimEnd().split("\n");
  } catch {
    return null;
  }
}

async function run(args: string[], options: { tty?: boolean; env?: Record<string, string> } = {}) {
  const stdout = new Capture(options.tty);
  const stderr = new Capture();
  const code = await sbox(["theme", ...args], {
    env: { HOME: home, PATH: `${bin()}:${process.env.PATH ?? ""}`, ...options.env },
    stdout,
    stderr,
    stdin: { isTTY: options.tty },
  });
  return { code, stdout: stdout.text, stderr: stderr.text };
}

async function savedTheme(): Promise<string | null> {
  try {
    return (await readFile(join(state(), "current"), "utf8")).trim();
  } catch {
    return null;
  }
}

const background = (r: number, g: number, b: number) => `\x1b[48;2;${r};${g};${b}m`;
const foreground = (r: number, g: number, b: number) => `\x1b[38;2;${r};${g};${b}m`;

describe("sbox theme --preview <name>", () => {
  test("prints truecolor swatches of the palette", async () => {
    // Act
    const result = await run(["--preview", "tokyo-night"]);

    // Assert
    expect(result.stderr).toBe("");
    expect(result.code).toBe(0);
    expect(result.stdout).toContain("tokyo-night");
    expect(result.stdout).toContain(background(0xf7, 0x76, 0x8e)); // red
    expect(result.stdout).toContain(background(0x9e, 0xce, 0x6a)); // green
    expect(result.stdout).toContain(background(0x7a, 0xa2, 0xf7)); // blue
    expect(result.stdout).toContain(background(0xff, 0x7a, 0x93)); // bright_red
    expect(result.stdout).toContain(background(0x0d, 0xb9, 0xd7)); // bright_cyan
  });

  test("prints a code and diff sample in the theme's colors on its background", async () => {
    // Act
    const result = await run(["--preview", "tokyo-night"]);

    // Assert
    const lines = result.stdout.split("\n");
    const sample = lines.filter((line) => line.startsWith(background(0x1a, 0x1b, 0x26)));
    expect(sample.length).toBeGreaterThan(4);
    const code = sample.join("\n");
    expect(code).toContain(foreground(0xad, 0x8e, 0xe6) + "function"); // magenta keyword
    expect(code).toContain(foreground(0x9e, 0xce, 0x6a) + '"'); // green string
    expect(code).toContain(foreground(0x41, 0x48, 0x68) + "//"); // muted comment
    expect(lines.some((line) => line.includes(foreground(0xf7, 0x76, 0x8e) + "-"))).toBe(true); // red removal
    expect(lines.some((line) => line.includes(foreground(0x9e, 0xce, 0x6a) + "+"))).toBe(true); // green addition
  });

  test("uses the colors of a light theme", async () => {
    // Act
    const tokyo = await run(["--preview", "tokyo-night"]);
    const white = await run(["--preview", "white"]);

    // Assert
    expect(white.code).toBe(0);
    expect(white.stdout).toContain("white");
    expect(white.stdout).not.toContain(background(0x1a, 0x1b, 0x26));
    expect(white.stdout).not.toBe(tokyo.stdout);
  });

  test("fails for an unknown theme", async () => {
    // Act
    const result = await run(["--preview", "no-such-theme"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toContain("there is no theme 'no-such-theme'");
    expect(result.stdout).toBe("");
  });
});
