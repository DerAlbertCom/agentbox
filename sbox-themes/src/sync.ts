import { existsSync, readdirSync } from "node:fs";
import { mkdir, readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

/**
 * A GitHub repository that themes are taken from: either a collection with one folder per
 * theme under `themes/` and its license at the root, or, with `theme`, a repository that is
 * one theme, with `colors.toml` and its license at the root.
 */
export interface ThemeSource {
  readonly repo: string;
  readonly ref: string;
  readonly theme?: string;
}

export interface SyncReport {
  readonly added: string[];
  readonly updated: string[];
  readonly unchanged: string[];
  /** Themes in the themes folder that no source has anymore. They are kept. */
  readonly removed: string[];
}

interface Download {
  /** The path in the themes folder. */
  readonly path: string;
  readonly content: string;
  /** The theme the file belongs to, or none for the license of a collection. */
  readonly theme?: string;
}

/**
 * Downloads the palettes of every source into the themes folder: adds new themes, updates
 * changed ones and never deletes. Everything is downloaded before anything is written.
 */
export async function syncThemes(options: {
  themes: string;
  sources: readonly ThemeSource[];
  fetch: typeof fetch;
}): Promise<SyncReport> {
  const downloads = (
    await Promise.all(options.sources.map((source) => download(source, options.fetch)))
  ).flat();

  const local = existsSync(options.themes)
    ? readdirSync(options.themes, { withFileTypes: true })
        .filter((entry) => entry.isDirectory())
        .map((entry) => entry.name)
    : [];
  const upstream = new Set(downloads.flatMap((file) => (file.theme ? [file.theme] : [])));
  const changed = new Set<string>();
  for (const file of downloads) {
    const target = join(options.themes, file.path);
    const current = existsSync(target) ? await readFile(target, "utf8") : undefined;
    if (current === file.content) continue;
    await mkdir(join(target, ".."), { recursive: true });
    await writeFile(target, file.content);
    if (file.theme) changed.add(file.theme);
  }

  const sorted = (names: Iterable<string>) => [...names].sort();
  return {
    added: sorted([...upstream].filter((name) => !local.includes(name))),
    updated: sorted([...changed].filter((name) => local.includes(name))),
    unchanged: sorted([...upstream].filter((name) => local.includes(name) && !changed.has(name))),
    removed: sorted(local.filter((name) => !upstream.has(name))),
  };
}

async function download(source: ThemeSource, fetch: typeof globalThis.fetch): Promise<Download[]> {
  const get = async (url: string) => {
    const response = await fetch(url);
    if (!response.ok)
      throw new Error(`${source.repo}@${source.ref}: GET ${url} failed with ${response.status}`);
    return response;
  };
  const raw = async (path: string) =>
    (await get(`https://raw.githubusercontent.com/${source.repo}/${source.ref}/${path}`)).text();

  const listing = (await (
    await get(`https://api.github.com/repos/${source.repo}/git/trees/${source.ref}?recursive=1`)
  ).json()) as { tree: { path: string; type: string }[]; truncated: boolean };
  if (listing.truncated) throw new Error(`${source.repo}@${source.ref}: the file tree is too large to list`);
  const files = new Set(listing.tree.filter((entry) => entry.type === "blob").map((entry) => entry.path));

  if (source.theme) {
    const theme = source.theme;
    return Promise.all(
      ["colors.toml", "LICENSE"]
        .filter((name) => files.has(name))
        .map(async (name) => ({ path: join(theme, name), content: await raw(name), theme })),
    );
  }

  const themes = [...files].flatMap((path) => path.match(/^themes\/([^/]+)\/colors\.toml$/)?.[1] ?? []);
  const perTheme = themes.flatMap((theme) =>
    ["colors.toml", "LICENSE"]
      .filter((name) => files.has(`themes/${theme}/${name}`))
      .map(async (name) => ({
        path: join(theme, name),
        content: await raw(`themes/${theme}/${name}`),
        theme,
      })),
  );
  const license = files.has("LICENSE")
    ? [raw("LICENSE").then((content) => ({ path: "LICENSE", content }))]
    : [];
  return Promise.all([...license, ...perTheme]);
}
