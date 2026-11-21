import { existsSync, readdirSync } from "node:fs";
import { join } from "node:path";
import type { Command } from "./api";

/** The commands folder: one folder per command, each holding its bundled `command.js`. */
export class Commands {
  constructor(private readonly folder: string) {}

  names(): string[] {
    if (!existsSync(this.folder)) return [];
    return readdirSync(this.folder, { withFileTypes: true })
      .filter((entry) => entry.isDirectory() && existsSync(this.module(entry.name)))
      .map((entry) => entry.name)
      .sort();
  }

  async load(name: string): Promise<Command> {
    const module = await import(this.module(name));
    return module.default as Command;
  }

  async overview(): Promise<string> {
    const names = this.names();
    if (names.length === 0) return "No commands are installed.\n";
    const width = Math.max(...names.map((name) => name.length));
    const lines = await Promise.all(
      names.map(async (name) => `  ${name.padEnd(width)}  ${(await this.load(name)).summary}\n`),
    );
    return "Commands:\n" + lines.join("");
  }

  folderOf(name: string): string {
    return join(this.folder, name);
  }

  private module(name: string): string {
    return join(this.folderOf(name), "command.js");
  }
}
