import { existsSync, readFileSync } from "node:fs";
import { mkdir, rename, writeFile } from "node:fs/promises";
import { dirname, join } from "node:path";
import { Liquid } from "liquidjs";
import { foldersWith } from "./folders";
import { channels, mix, type Palette } from "./palette";

/** An app template: `<apps>/<app>/app.yaml` next to the app's Liquid templates. */
export interface AppTemplate {
  readonly name: string;
  readonly folder: string;
  /** Templates rendered to target files. */
  readonly files: readonly { template: string; target: string }[];
  /** JSON objects merged into existing settings files; string values are Liquid templates. */
  readonly settings: readonly { file: string; merge: Record<string, unknown> }[];
  /** Shell commands run after rendering, from the home folder. */
  readonly run: readonly string[];
}

export interface Rendered {
  /** Run commands that failed, with their output. */
  readonly failures: string[];
}

export function readAppTemplates(folder: string): AppTemplate[] {
  return foldersWith(folder, "app.yaml").map((name) => readAppTemplate(name, join(folder, name)));
}

function readAppTemplate(name: string, folder: string): AppTemplate {
  const fail = (message: string): never => {
    throw new Error(`${join(folder, "app.yaml")}: ${message}`);
  };
  const yaml = (Bun.YAML.parse(readFileSync(join(folder, "app.yaml"), "utf8")) ?? {}) as Record<
    string,
    unknown
  >;
  if (typeof yaml !== "object" || Array.isArray(yaml)) fail("expected a mapping");
  const unknown = Object.keys(yaml).filter((key) => !["files", "settings", "run"].includes(key));
  if (unknown.length) fail(`unknown key '${unknown[0]}'`);

  const list = (key: string) => {
    const value = yaml[key] ?? [];
    if (!Array.isArray(value)) fail(`'${key}' must be a list`);
    return value as unknown[];
  };
  const entry = (key: string, item: unknown, fields: Record<string, "string" | "object">) => {
    if (typeof item !== "object" || item === null || Array.isArray(item))
      fail(`every '${key}' entry must be a mapping`);
    const record = item as Record<string, unknown>;
    for (const field of Object.keys(record)) if (!(field in fields)) fail(`unknown key '${key}.${field}'`);
    for (const [field, type] of Object.entries(fields)) {
      const value = record[field];
      const ok =
        type === "string"
          ? typeof value === "string" && value !== ""
          : typeof value === "object" && value !== null && !Array.isArray(value);
      if (!ok) fail(`'${key}.${field}' must be a ${type === "string" ? "non-empty string" : "mapping"}`);
    }
    return record;
  };

  const files = list("files").map(
    (item) =>
      entry("files", item, { template: "string", target: "string" }) as AppTemplate["files"][number],
  );
  const settings = list("settings").map(
    (item) =>
      entry("settings", item, { file: "string", merge: "object" }) as AppTemplate["settings"][number],
  );
  const run = list("run").map((item) =>
    typeof item === "string" && item !== "" ? item : fail("every 'run' entry must be a command"),
  );
  for (const target of [...files.map((file) => file.target), ...settings.map((setting) => setting.file)]) {
    if (!target.startsWith("~/")) fail(`'${target}' must start with ~/`);
  }
  return { name, folder, files, settings, run };
}

/** Renders the app templates with the palette: writes the files, merges the settings, runs the commands. */
export async function renderAppTemplates(
  apps: readonly AppTemplate[],
  palette: Palette,
  env: Readonly<Record<string, string | undefined>>,
  home: string,
): Promise<Rendered> {
  const failures: string[] = [];
  for (const app of apps) {
    const liquid = engine(app.folder);
    const target = (path: string) => join(home, path.slice(2));

    for (const file of app.files) {
      const text = await liquid.renderFile(file.template, palette).catch((error: Error) => {
        throw new Error(`${app.name}/${file.template}: ${error.message}`);
      });
      await writeAtomically(target(file.target), text);
    }

    for (const setting of app.settings) {
      const path = target(setting.file);
      const current = existsSync(path) ? parseSettings(path) : {};
      const merge = (await renderStrings(liquid, setting.merge, palette)) as Record<string, unknown>;
      await writeAtomically(path, JSON.stringify(deepMerge(current, merge), null, 2) + "\n");
    }

    for (const command of app.run) {
      const result = Bun.spawnSync(["sh", "-c", command], {
        cwd: home,
        env: { ...env },
        stdout: "pipe",
        stderr: "pipe",
      });
      if (result.exitCode !== 0) {
        const output = (result.stderr.toString() + result.stdout.toString()).trim();
        failures.push(
          `${app.name}: '${command}' failed with exit code ${result.exitCode}${output ? `:\n${output}` : ""}`,
        );
      }
    }
  }
  return { failures };
}

function engine(root: string): Liquid {
  const liquid = new Liquid({ root, strictFilters: true, strictVariables: true });
  liquid.registerFilter("mix", (color: string, other: string, amount: number | string) =>
    mix(color, other, amount),
  );
  liquid.registerFilter("strip", (color: string) => String(color).replace(/^#/, ""));
  liquid.registerFilter("rgb", (color: string) => channels(color).join(","));
  return liquid;
}

async function renderStrings(liquid: Liquid, value: unknown, palette: Palette): Promise<unknown> {
  if (typeof value === "string") return liquid.parseAndRender(value, palette);
  if (Array.isArray(value)) return Promise.all(value.map((item) => renderStrings(liquid, item, palette)));
  if (typeof value === "object" && value !== null) {
    const entries = await Promise.all(
      Object.entries(value).map(async ([key, item]) => [key, await renderStrings(liquid, item, palette)]),
    );
    return Object.fromEntries(entries);
  }
  return value;
}

function parseSettings(path: string): Record<string, unknown> {
  const parsed = JSON.parse(readFileSync(path, "utf8"));
  if (typeof parsed !== "object" || parsed === null || Array.isArray(parsed))
    throw new Error(`${path} does not hold a JSON object`);
  return parsed;
}

function deepMerge(
  target: Record<string, unknown>,
  source: Record<string, unknown>,
): Record<string, unknown> {
  const merged = { ...target };
  for (const [key, value] of Object.entries(source)) {
    const current = merged[key];
    merged[key] = isObject(current) && isObject(value) ? deepMerge(current, value) : value;
  }
  return merged;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null && !Array.isArray(value);
}

async function writeAtomically(path: string, text: string) {
  await mkdir(dirname(path), { recursive: true });
  const temporary = `${path}.sbox-${crypto.randomUUID()}`;
  await writeFile(temporary, text);
  await rename(temporary, path);
}
