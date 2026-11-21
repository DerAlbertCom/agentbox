import { existsSync, readFileSync } from "node:fs";
import { join } from "node:path";

/** The colors of a theme by key, after the fallbacks, plus `mode` ("dark" or "light"). */
export type Palette = Record<string, string>;

const hex = /^#[0-9a-f]{6}$/i;

/** Reads `<folder>/colors.toml` and resolves it the way upstream does. */
export function readPalette(folder: string): Palette {
  const parsed = Bun.TOML.parse(readFileSync(join(folder, "colors.toml"), "utf8")) as Record<string, unknown>;
  const colors: Palette = {};
  for (const [key, value] of Object.entries(parsed)) {
    if (typeof value === "string" && value !== "") colors[key] = value;
  }
  return resolve(colors, existsSync(join(folder, "light.mode")));
}

/** Blends `color` toward `other` by `amount`: a percentage such as 15 or "15%", or a fraction up to 1. */
export function mix(color: string, other: string, amount: number | string): string {
  for (const value of [color, other]) {
    if (!hex.test(value)) throw new Error(`mix needs colors like #rrggbb, not '${value}'`);
  }
  let t = typeof amount === "number" ? amount : Number.parseFloat(amount);
  if (Number.isNaN(t)) throw new Error(`mix needs an amount like 15%, not '${amount}'`);
  if ((typeof amount === "string" && amount.trim().endsWith("%")) || t > 1) t /= 100;
  t = Math.min(Math.max(t, 0), 1);
  const a = channels(color);
  const b = channels(other);
  return (
    "#" +
    a
      .map((value, i) =>
        Math.floor(value * (1 - t) + b[i]! * t + 0.5)
          .toString(16)
          .padStart(2, "0"),
      )
      .join("")
  );
}

/** The decimal channels of a #rrggbb color. */
export function channels(color: string): number[] {
  if (!hex.test(color)) throw new Error(`expected a color like #rrggbb, not '${color}'`);
  return [1, 3, 5].map((start) => Number.parseInt(color.slice(start, start + 2), 16));
}

const legacyNames = [
  ["background", "bg"],
  ["dark_background", "dark_bg"],
  ["darker_background", "darker_bg"],
  ["lighter_background", "lighter_bg"],
  ["foreground", "fg"],
  ["dark_foreground", "dark_fg"],
  ["light_foreground", "light_fg"],
  ["bright_foreground", "bright_fg"],
] as const;

// The cascade of upstream's theme color resolution: every step only fills keys that are still empty.
function resolve(c: Palette, lightModeFile: boolean): Palette {
  const fill = (key: string, ...sources: (string | undefined)[]) => {
    if (c[key]) return;
    const value = sources.find((source) => source);
    if (value) c[key] = value;
  };
  for (const [canonical, short] of legacyNames) fill(canonical, c[short]);

  fill("background", c.color0);
  fill("foreground", c.color7);
  if (c.background) c.color0 = c.background;
  if (c.foreground) c.color7 = c.foreground;

  const ansi = ["", "red", "green", "yellow", "blue", "magenta", "cyan"];
  ansi.forEach((name, i) => {
    if (!name) return;
    fill(name, c[`color${i}`]);
    fill(`bright_${name}`, c[`color${i + 8}`]);
  });
  fill("magenta", c.purple);
  fill("bright_magenta", c.bright_purple);

  fill("light_foreground", c.color7, c.foreground);
  fill("bright_foreground", c.color15, c.foreground);
  if (c.bright_foreground) c.cursor = c.bright_foreground;
  fill("lighter_background", c.color0, c.background);
  fill("dark_foreground", c.color8, c.foreground);
  fill("muted", c.color8, c.dark_foreground);
  fill("selection", c.selection_background, c.color8, c.color0, c.background);
  fill("selection_background", c.selection);
  fill("selection_foreground", c.bright_foreground);
  fill("orange", c.yellow);
  fill("brown", mixed(c.orange, "#000000", 50));

  fill("dark_background", mixed(c.background, "#000000", 25));
  fill("darker_background", mixed(c.background, "#000000", 50));
  for (const name of ansi.slice(1)) fill(`bright_${name}`, mixed(c[name], "#ffffff", 20));
  fill("purple", c.magenta);
  fill("bright_purple", c.bright_magenta);

  const aliases = ["background", "red", "green", "yellow", "blue", "magenta", "cyan", "foreground", "muted"];
  aliases.forEach((name, i) => fill(`color${i}`, c[name]));
  ["red", "green", "yellow", "blue", "magenta", "cyan"].forEach((name, i) =>
    fill(`color${i + 9}`, c[`bright_${name}`]),
  );
  fill("color15", c.bright_foreground);
  fill("accent", c.color4);

  for (const [canonical, short] of legacyNames) if (c[canonical]) c[short] = c[canonical];

  fill("mode", c.theme_type, lightModeFile ? "light" : undefined, modeOf(c.background));
  c.theme_type = c.mode!;
  return c;
}

function mixed(color: string | undefined, other: string, amount: number): string | undefined {
  return color && hex.test(color) ? mix(color, other, amount) : undefined;
}

function modeOf(background: string | undefined): string {
  if (!background || !hex.test(background)) return "dark";
  const sum = channels(background).reduce((total, value) => total + value, 0);
  return sum > 382 ? "light" : "dark";
}
