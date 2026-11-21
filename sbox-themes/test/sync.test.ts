import { afterEach, beforeEach, describe, expect, test } from "bun:test";
import { mkdir, mkdtemp, readdir, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { syncThemes, type ThemeSource } from "../src/sync";

let themes: string;

beforeEach(async () => {
  themes = join(await mkdtemp(join(tmpdir(), "sync-themes-")), "themes");
  await mkdir(themes);
});

afterEach(async () => {
  await rm(join(themes, ".."), { recursive: true, force: true });
});

const collection: ThemeSource = { repo: "example/collection", ref: "main" };
const single: ThemeSource = { repo: "someone/jade-theme", ref: "trunk", theme: "jade" };

/** A GitHub stand-in serving the trees API and raw files of the given repositories. */
function gitHub(repos: Record<string, Record<string, string>>): typeof fetch {
  return (async (input: string | URL | Request) => {
    const url = String(input);
    const tree = url.match(
      /^https:\/\/api\.github\.com\/repos\/([^/]+\/[^/]+)\/git\/trees\/([^?]+)\?recursive=1$/,
    );
    if (tree) {
      const files = repos[`${tree[1]}@${tree[2]}`];
      if (!files) return new Response("not found", { status: 404 });
      const paths = Object.keys(files).flatMap((path) => [path, ...parents(path)]);
      const entries = [...new Set(paths)].map((path) => ({ path, type: path in files ? "blob" : "tree" }));
      return Response.json({ tree: entries, truncated: false });
    }
    const raw = url.match(/^https:\/\/raw\.githubusercontent\.com\/([^/]+\/[^/]+)\/([^/]+)\/(.+)$/);
    const content = raw && repos[`${raw[1]}@${raw[2]}`]?.[raw[3]!];
    return content === undefined || content === null
      ? new Response("not found", { status: 404 })
      : new Response(content);
  }) as typeof fetch;
}

function parents(path: string): string[] {
  const parts = path.split("/").slice(0, -1);
  return parts.map((_, index) => parts.slice(0, index + 1).join("/"));
}

async function local(path: string) {
  return readFile(join(themes, path), "utf8");
}

describe("syncThemes", () => {
  test("adds every theme with the licenses and reports them", async () => {
    // Arrange
    const fetch = gitHub({
      "example/collection@main": {
        LICENSE: "MIT, the collection\n",
        "themes/nord/colors.toml": 'background = "#2e3440"\n',
        "themes/nord/preview.png": "png",
        "themes/dusk/colors.toml": 'background = "#101010"\n',
        "themes/dusk/LICENSE": "MIT, dusk\n",
        "docs/colors.toml": "not a theme",
      },
      "someone/jade-theme@trunk": {
        LICENSE: "MIT, jade\n",
        "colors.toml": 'background = "#111c18"\n',
        "README.md": "jade",
      },
    });

    // Act
    const report = await syncThemes({ themes, sources: [collection, single], fetch });

    // Assert
    expect(report).toEqual({ added: ["dusk", "jade", "nord"], updated: [], unchanged: [], removed: [] });
    expect(await readdir(themes)).toEqual(expect.arrayContaining(["LICENSE", "dusk", "jade", "nord"]));
    expect(await local("LICENSE")).toBe("MIT, the collection\n");
    expect(await local("nord/colors.toml")).toBe('background = "#2e3440"\n');
    expect(await readdir(join(themes, "nord"))).toEqual(["colors.toml"]);
    expect(await local("dusk/LICENSE")).toBe("MIT, dusk\n");
    expect(await local("jade/colors.toml")).toBe('background = "#111c18"\n');
    expect(await local("jade/LICENSE")).toBe("MIT, jade\n");
  });

  test("updates changed themes, keeps the ones removed upstream and reports both", async () => {
    // Arrange
    await mkdir(join(themes, "nord"));
    await writeFile(join(themes, "nord/colors.toml"), 'background = "#000000"\n');
    await mkdir(join(themes, "dusk"));
    await writeFile(join(themes, "dusk/colors.toml"), 'background = "#101010"\n');
    await mkdir(join(themes, "gone"));
    await writeFile(join(themes, "gone/colors.toml"), 'background = "#202020"\n');
    const fetch = gitHub({
      "example/collection@main": {
        LICENSE: "MIT, the collection\n",
        "themes/nord/colors.toml": 'background = "#2e3440"\n',
        "themes/dusk/colors.toml": 'background = "#101010"\n',
      },
    });

    // Act
    const report = await syncThemes({ themes, sources: [collection], fetch });

    // Assert
    expect(report).toEqual({ added: [], updated: ["nord"], unchanged: ["dusk"], removed: ["gone"] });
    expect(await local("nord/colors.toml")).toBe('background = "#2e3440"\n');
    expect(await local("gone/colors.toml")).toBe('background = "#202020"\n');
  });

  test("fails without touching the themes when a source cannot be read", async () => {
    // Arrange
    const fetch = gitHub({ "example/collection@main": { "themes/nord/colors.toml": "x" } });

    // Act
    const sync = syncThemes({ themes, sources: [collection, single], fetch });

    // Assert
    await expect(sync).rejects.toThrow("someone/jade-theme");
    expect(await readdir(themes)).toEqual([]);
  });
});
