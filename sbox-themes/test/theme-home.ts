import { afterEach, beforeEach } from "bun:test";
import { cp, mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
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

/**
 * A temporary home for each test with the theme command installed by `install`. Fakes of the
 * tools the apps run are first on PATH: `bat` logs its arguments to `~/bat.log` and copies the
 * bat themes it finds to `~/bat.cache`.
 */
export function useThemeHome() {
  let home = "";

  beforeEach(async () => {
    home = await mkdtemp(join(tmpdir(), "sbox-theme-"));
    await mkdir(tools(), { recursive: true });
    await writeFile(
      join(tools(), "bat"),
      `#!/bin/sh\necho "$PWD: bat $@" >> bat.log\ncp .config/bat/themes/*.tmTheme bat.cache 2>/dev/null || true\n`,
      { mode: 0o755 },
    );
  });

  afterEach(async () => {
    await rm(home, { recursive: true, force: true });
  });

  const data = () => join(home, ".local/share/sbox/commands/theme");
  const state = () => join(home, ".local/state/sbox/theme");
  const tools = () => join(home, ".fake-tools");
  const homeFile = (path: string) => readFile(join(home, path), "utf8");

  return {
    get path() {
      return home;
    },

    /**
     * Installs the theme command with the palettes and app templates of this package, or with
     * the given ones: files by their path in the command's data folder.
     */
    async install(files?: Record<string, string>) {
      await mkdir(data(), { recursive: true });
      await writeFile(
        join(data(), "command.js"),
        `export { default } from ${JSON.stringify(join(packageFolder, "src/command.ts"))};\n`,
      );
      if (!files) {
        await cp(join(packageFolder, "themes"), join(data(), "themes"), { recursive: true });
        await cp(join(packageFolder, "apps"), join(data(), "apps"), { recursive: true });
        return;
      }
      for (const [path, content] of Object.entries(files)) {
        await mkdir(join(data(), path, ".."), { recursive: true });
        await writeFile(join(data(), path), content);
      }
    },

    async save(theme: string) {
      await mkdir(state(), { recursive: true });
      await writeFile(join(state(), "current"), theme + "\n");
    },

    /** Runs `sbox theme`; with `tty` its stdin and stdout are terminals. */
    async run(args: string[], env: Record<string, string> = {}, options: { tty?: boolean } = {}) {
      const stdout = new Capture(options.tty);
      const stderr = new Capture();
      const code = await sbox(["theme", ...args], {
        env: { HOME: home, PATH: `${tools()}:${process.env.PATH ?? ""}`, ...env },
        stdout,
        stderr,
        stdin: { isTTY: options.tty },
      });
      return { code, stdout: stdout.text, stderr: stderr.text };
    },

    async write(path: string, text: string) {
      await mkdir(join(home, path, ".."), { recursive: true });
      await writeFile(join(home, path), text);
    },

    file: homeFile,

    async json(path: string) {
      return JSON.parse(await homeFile(path));
    },

    exists(path: string) {
      return Bun.file(join(home, path)).exists();
    },
  };
}
