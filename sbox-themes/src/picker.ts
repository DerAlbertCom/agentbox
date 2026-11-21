import type { CommandContext } from "sbox-cli";
import { channels, mix, type Palette } from "./palette";

const reset = "\x1b[0m";
const bold = "\x1b[1m";

const background = (color: string) => `\x1b[48;2;${channels(color).join(";")}m`;
const foreground = (color: string) => `\x1b[38;2;${channels(color).join(";")}m`;

/** A run of text in one palette color. */
type Segment = [key: string, text: string];

const ansi = (from: number) => Array.from({ length: 8 }, (_, i) => `color${from + i}`);
const swatchRows: [label: string, keys: string[]][] = [
  ["normal", ansi(0)],
  ["bright", ansi(8)],
];
const uiKeys = ["accent", "selection", "muted", "orange", "brown"];

const code: Segment[][] = [
  [["muted", "// Greets everyone in the sandbox"]],
  [["magenta", "function "], ["blue", "greet"], ["foreground", "(name: "], ["cyan", "string"], ["foreground", ") {"]],
  [["magenta", "  const "], ["foreground", "count = "], ["orange", "42"], ["foreground", ";"]],
  [["magenta", "  return "], ["green", '"Hello, "'], ["foreground", " + name;"]],
  [["foreground", "}"]],
];

const diff: [kind: "context" | "removed" | "added", Segment[]][] = [
  ["context", [["cyan", "@@ -1,3 +1,3 @@"]]],
  ["context", [["foreground", "  theme:"]]],
  ["removed", [["red", "- "], ["foreground", 'name: "old"']]],
  ["added", [["green", "+ "], ["foreground", 'name: "new"']]],
];

/**
 * The preview of a theme for the picker: its name, truecolor swatches of the palette and a
 * code and diff sample in its colors on its background, `width` columns wide.
 */
export function preview(name: string, palette: Palette, width: number): string {
  const lines = [`${bold}${name}${reset} (${palette.mode})`, ""];
  for (const [label, row] of swatchRows) {
    const swatches = row.map((key) => `${background(palette[key]!)}    ${reset}`);
    lines.push(`${label.padEnd(7)}${swatches.join(" ")}`);
  }
  lines.push(
    `${"ui".padEnd(7)}${uiKeys.map((key) => `${background(palette[key]!)}    ${reset} ${key}`).join("  ")}`,
    "",
  );

  const bg = palette.background!;
  const sample = (segments: Segment[], lineBackground = bg) => {
    const text = segments.map(([key, part]) => foreground(palette[key]!) + part).join("");
    const length = segments.reduce((total, [, part]) => total + part.length, 1);
    return `${background(lineBackground)} ${text}${" ".repeat(Math.max(width - length, 0))}${reset}`;
  };
  const blank = sample([]);
  lines.push(blank, ...code.map((segments) => sample(segments)), blank);
  for (const [kind, segments] of diff) {
    const lineBackground =
      kind === "removed" ? mix(bg, palette.red!, 20) : kind === "added" ? mix(bg, palette.green!, 20) : bg;
    lines.push(sample(segments, lineBackground));
  }
  lines.push(blank);
  return lines.join("\n") + "\n";
}

/**
 * Lets the user choose a theme with fzf, previewing each through `sbox <command> --preview`.
 * Returns undefined when the choice is cancelled.
 */
export async function pick(themes: string[], current: string, ctx: CommandContext): Promise<string | undefined> {
  const fzf = Bun.which("fzf", { PATH: ctx.env.PATH ?? "" });
  if (!fzf) throw new Error("the picker needs fzf; choose a theme with 'sbox theme <name>' instead.");
  const env = Object.fromEntries(
    Object.entries(ctx.env).filter((entry): entry is [string, string] => entry[1] !== undefined),
  );
  const picker = Bun.spawn(
    [
      fzf,
      "--prompt=theme> ",
      "--no-multi",
      `--preview=sbox ${ctx.name} --preview {}`,
      "--preview-window=right,70%",
      "--bind",
      `load:pos(${themes.indexOf(current) + 1})`,
    ],
    { env, stdin: new Blob([themes.join("\n") + "\n"]), stdout: "pipe", stderr: "inherit" },
  );
  const [selection, code] = await Promise.all([new Response(picker.stdout).text(), picker.exited]);
  if (code === 1 || code === 130) return undefined;
  if (code !== 0) throw new Error(`fzf failed with exit code ${code}.`);
  return selection.trim() || undefined;
}
