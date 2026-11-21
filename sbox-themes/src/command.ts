import { existsSync, readdirSync, readFileSync } from "node:fs";
import { mkdir, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { parseArgs } from "node:util";
import type { Command, CommandContext } from "sbox-cli";
import { readApps, renderApps, type Rendered } from "./apps";
import { readPalette } from "./palette";

const fallbackTheme = "tokyo-night";

export default {
  summary: "Switch the color theme of the sandbox's apps",
  async run(args, ctx) {
    const { values, positionals } = parseArgs({
      args,
      options: {
        list: { type: "boolean" },
        current: { type: "boolean" },
        apply: { type: "boolean" },
      },
      allowPositionals: true,
    });
    if (positionals.length > 1) throw new Error("expected one theme name");
    const themes = new Themes(ctx);

    if (values.current) {
      ctx.stdout.write(themes.resolve().name + "\n");
      return 0;
    }
    if (values.apply) {
      const resolved = themes.resolve();
      if (resolved.warning) ctx.stderr.write(ctx.style.yellow(`sbox theme: ${resolved.warning}`) + "\n");
      const rendered = await themes.render(resolved.name);
      ctx.stdout.write(`Applied the theme '${resolved.name}'.\n`);
      return rendered.failures.length ? 1 : 0;
    }
    const name = positionals[0];
    if (values.list || name === undefined) {
      const current = themes.resolve().name;
      for (const theme of themes.names()) {
        ctx.stdout.write(theme === current ? ctx.style.bold(`* ${theme}`) + "\n" : `  ${theme}\n`);
      }
      return 0;
    }
    if (!themes.names().includes(name)) {
      ctx.stderr.write(`sbox theme: there is no theme '${name}'. The themes are:\n`);
      for (const theme of themes.names()) ctx.stderr.write(`  ${theme}\n`);
      return 1;
    }
    const rendered = await themes.render(name);
    await themes.save(name);
    const restart = rendered.restart.length ? ` Restart ${listing(rendered.restart)} to see it.` : "";
    ctx.stdout.write(`Switched to the theme '${name}'.${restart}\n`);
    return rendered.failures.length ? 1 : 0;
  },
} satisfies Command;

class Themes {
  private readonly themes: string;
  private readonly saved: string;

  constructor(private readonly ctx: CommandContext) {
    this.themes = join(ctx.paths.data, "themes");
    this.saved = join(ctx.paths.state, "current");
  }

  names(): string[] {
    if (!existsSync(this.themes)) return [];
    return readdirSync(this.themes, { withFileTypes: true })
      .filter((entry) => entry.isDirectory() && existsSync(join(this.themes, entry.name, "colors.toml")))
      .map((entry) => entry.name)
      .sort();
  }

  /** The saved choice, then the kit's default `SBOX_THEME`, then tokyo-night. */
  resolve(): { name: string; warning?: string } {
    const names = this.names();
    const saved = existsSync(this.saved) ? readFileSync(this.saved, "utf8").trim() : "";
    const fallback =
      [this.ctx.env.SBOX_THEME, fallbackTheme].find((name) => name && names.includes(name)) ?? fallbackTheme;
    if (!saved) return { name: fallback };
    if (names.includes(saved)) return { name: saved };
    return { name: fallback, warning: `the saved theme '${saved}' is not installed; using '${fallback}'.` };
  }

  /** Renders every app with the theme and reports the failed run commands. */
  async render(name: string): Promise<Rendered> {
    const palette = readPalette(join(this.themes, name));
    const apps = readApps(join(this.ctx.paths.data, "apps"));
    const rendered = await renderApps(apps, palette, this.ctx.env, this.ctx.paths.home);
    for (const failure of rendered.failures) this.ctx.stderr.write(`sbox theme: ${failure}\n`);
    return rendered;
  }

  async save(name: string) {
    await mkdir(this.ctx.paths.state, { recursive: true });
    await writeFile(this.saved, name + "\n");
  }
}

function listing(names: string[]): string {
  return names.length > 1 ? `${names.slice(0, -1).join(", ")} and ${names.at(-1)}` : names.join("");
}
