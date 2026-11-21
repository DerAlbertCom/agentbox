import { existsSync, readFileSync } from "node:fs";
import { mkdir, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { parseArgs } from "node:util";
import type { Command, CommandContext } from "sbox-cli";
import { readAppTemplates, renderAppTemplates, type Rendered } from "./apps";
import { foldersWith } from "./folders";
import { readPalette, type Palette } from "./palette";
import { pick, preview } from "./picker";

const fallbackTheme = "tokyo-night";
/** The terminal color script, rendered by the `terminal` app template. */
const terminalColorScript = ".config/sbox/terminal-colors.sh";

export default {
  summary: "Switch the color theme of the sandbox's apps",
  async run(args, ctx) {
    const { values, positionals } = parseArgs({
      args,
      options: {
        list: { type: "boolean" },
        current: { type: "boolean" },
        apply: { type: "boolean" },
        preview: { type: "string" },
      },
      allowPositionals: true,
    });
    if (positionals.length > 1) throw new Error("expected one theme name");
    const themes = new Themes(ctx);

    if (values.current) {
      ctx.stdout.write(themes.resolve().name + "\n");
      return 0;
    }
    if (values.preview !== undefined) {
      if (!themes.exists(values.preview)) return themes.unknown(values.preview);
      const width = Number.parseInt(ctx.env.FZF_PREVIEW_COLUMNS ?? "", 10) || 60;
      ctx.stdout.write(preview(values.preview, themes.palette(values.preview), width));
      return 0;
    }
    if (values.apply) {
      const resolved = themes.resolve();
      for (const warning of resolved.warnings) {
        ctx.stderr.write(ctx.style.yellow(`sbox theme: ${warning}`) + "\n");
      }
      const rendered = await themes.render(resolved.name);
      ctx.stdout.write(`Applied the theme '${resolved.name}'.\n`);
      return rendered.failures.length ? 1 : 0;
    }
    let name = positionals[0];
    if (name === undefined && !values.list && ctx.isTTY) {
      name = await pick(themes.names(), themes.resolve().name, ctx);
      if (name === undefined) return 0;
    }
    if (values.list || name === undefined) {
      const current = themes.resolve().name;
      for (const theme of themes.names()) {
        ctx.stdout.write(theme === current ? ctx.style.bold(`* ${theme}`) + "\n" : `  ${theme}\n`);
      }
      return 0;
    }
    if (!themes.exists(name)) return themes.unknown(name);
    const rendered = await themes.render(name);
    await themes.save(name);
    if (ctx.env.SBOX_TERMINAL_THEME === "1" && ctx.stdout.isTTY) themes.recolorTerminal();
    ctx.stdout.write(`Switched to the theme '${name}'. Some apps show it only after a restart.\n`);
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
    return foldersWith(this.themes, "colors.toml");
  }

  exists(name: string): boolean {
    return this.names().includes(name);
  }

  /** Reports an unknown theme with the available ones and returns the exit code. */
  unknown(name: string): number {
    this.ctx.stderr.write(`sbox theme: there is no theme '${name}'. The themes are:\n`);
    for (const theme of this.names()) this.ctx.stderr.write(`  ${theme}\n`);
    return 1;
  }

  palette(name: string): Palette {
    return readPalette(join(this.themes, name));
  }

  /** The saved choice, then the kit's default `SBOX_THEME`, then tokyo-night. */
  resolve(): { name: string; warnings: string[] } {
    const names = this.names();
    const saved = existsSync(this.saved) ? readFileSync(this.saved, "utf8").trim() : "";
    if (saved && names.includes(saved)) return { name: saved, warnings: [] };

    const preset = this.ctx.env.SBOX_THEME;
    const fallback = preset && names.includes(preset) ? preset : fallbackTheme;
    const warnings: string[] = [];
    if (saved) warnings.push(`the saved theme '${saved}' is not installed; using '${fallback}'.`);
    if (preset && preset !== fallback)
      warnings.push(`the default theme '${preset}' (SBOX_THEME) is not installed; using '${fallback}'.`);
    return { name: fallback, warnings };
  }

  /** Renders every app with the theme and reports the failed run commands. */
  async render(name: string): Promise<Rendered> {
    const palette = this.palette(name);
    const apps = readAppTemplates(join(this.ctx.paths.data, "apps"));
    const rendered = await renderAppTemplates(apps, palette, this.ctx.env, this.ctx.paths.home);
    for (const failure of rendered.failures) this.ctx.stderr.write(`sbox theme: ${failure}\n`);
    return rendered;
  }

  /** Prints the colors of the terminal color script, if it is rendered and runs. */
  recolorTerminal() {
    const script = join(this.ctx.paths.home, terminalColorScript);
    if (!existsSync(script)) return;
    const result = Bun.spawnSync(["sh", script], { stdout: "pipe", stderr: "ignore" });
    if (result.exitCode === 0) this.ctx.stdout.write(result.stdout.toString());
  }

  async save(name: string) {
    await mkdir(this.ctx.paths.state, { recursive: true });
    await writeFile(this.saved, name + "\n");
  }
}
