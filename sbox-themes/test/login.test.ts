import { afterEach, beforeEach, describe, expect, test } from "bun:test";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

const loginScript = join(import.meta.dir, "../profile/sbox-terminal-theme.sh");
const resets = "\x1b]104\x07\x1b]110\x07\x1b]111\x07\x1b]112\x07";

let home: string;

beforeEach(async () => {
  home = await mkdtemp(join(tmpdir(), "sbox-login-"));
  await mkdir(join(home, "bin"));
  await writeFile(join(home, "bin/sbox"), `#!/bin/sh\necho "sbox $@" >> "${home}/sbox.log"\n`, { mode: 0o755 });
});

afterEach(async () => {
  await rm(home, { recursive: true, force: true });
});

async function colorScript(text: string) {
  await mkdir(join(home, ".config/sbox"), { recursive: true });
  await writeFile(join(home, ".config/sbox/terminal-colors.sh"), text);
}

/** Runs a bash that sources the login script, prints `shell-ok` and exits; returns all it printed. */
async function login(options: { env?: Record<string, string>; tty: boolean }): Promise<string> {
  const command = ["bash", "--noprofile", "--norc", "-c", `. "${loginScript}"; echo shell-ok`];
  const env = { HOME: home, PATH: `${join(home, "bin")}:${process.env.PATH ?? ""}`, ...options.env };
  if (!options.tty) {
    const result = Bun.spawnSync(command, { env, stdout: "pipe", stderr: "pipe" });
    return result.stdout.toString() + result.stderr.toString();
  }
  let output = "";
  const decoder = new TextDecoder();
  const shell = Bun.spawn(command, {
    env,
    terminal: { cols: 80, rows: 24, data: (_terminal, data) => (output += decoder.decode(data)) },
  });
  await shell.exited;
  shell.terminal?.close();
  return output;
}

describe("the login script", () => {
  test("sets the terminal colors at login and resets them when the shell exits", async () => {
    // Arrange
    await colorScript("printf COLORS\n");

    // Act
    const output = await login({ env: { SBOX_TERMINAL_THEME: "1" }, tty: true });

    // Assert
    expect(output).toBe("COLORSshell-ok\r\n" + resets);
    expect(await Bun.file(join(home, "sbox.log")).exists()).toBe(false);
  });

  test("does nothing without SBOX_TERMINAL_THEME=1", async () => {
    // Arrange
    await colorScript("printf COLORS\n");

    // Act
    const unset = await login({ tty: true });
    const off = await login({ env: { SBOX_TERMINAL_THEME: "0" }, tty: true });

    // Assert
    expect(unset).toBe("shell-ok\r\n");
    expect(off).toBe("shell-ok\r\n");
  });

  test("does nothing when the output is not a terminal", async () => {
    // Arrange
    await colorScript("printf COLORS\n");

    // Act
    const output = await login({ env: { SBOX_TERMINAL_THEME: "1" }, tty: false });

    // Assert
    expect(output).toBe("shell-ok\n");
  });

  test("does nothing before the terminal color script is rendered", async () => {
    // Act
    const output = await login({ env: { SBOX_TERMINAL_THEME: "1" }, tty: true });

    // Assert
    expect(output).toBe("shell-ok\r\n");
  });

  test("keeps the shell usable and quiet when the terminal color script fails", async () => {
    // Arrange
    await colorScript("echo broken >&2\nexit 3\n");

    // Act
    const output = await login({ env: { SBOX_TERMINAL_THEME: "1" }, tty: true });

    // Assert
    expect(output).not.toContain("broken");
    expect(output).toStartWith("shell-ok\r\n");
  });
});
