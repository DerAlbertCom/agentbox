import { afterEach, beforeEach, describe, expect, test } from "bun:test";
import { cp, mkdir, mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { sbox } from "../../sbox-cli/src/sbox";

class Capture {
  text = "";
  write(chunk: string) {
    this.text += chunk;
  }
}

const packageFolder = join(import.meta.dir, "..");

let home: string;

beforeEach(async () => {
  home = await mkdtemp(join(tmpdir(), "sbox-theme-"));
});

afterEach(async () => {
  await rm(home, { recursive: true, force: true });
});

const data = () => join(home, ".local/share/sbox/commands/theme");
const state = () => join(home, ".local/state/sbox/theme");

/**
 * Installs the theme command with the palettes and app definitions of this package, or with
 * the given ones: files by their path in the command's data folder.
 */
async function install(files?: Record<string, string>) {
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
}

async function save(theme: string) {
  await mkdir(state(), { recursive: true });
  await writeFile(join(state(), "current"), theme + "\n");
}

async function run(args: string[], env: Record<string, string> = {}) {
  const stdout = new Capture();
  const stderr = new Capture();
  const code = await sbox(["theme", ...args], {
    env: { HOME: home, PATH: process.env.PATH ?? "", ...env },
    stdout,
    stderr,
    stdin: {},
  });
  return { code, stdout: stdout.text, stderr: stderr.text };
}

async function homeFile(path: string) {
  return readFile(join(home, path), "utf8");
}

async function homeJson(path: string) {
  return JSON.parse(await homeFile(path));
}

describe("sbox theme <name>", () => {
  test("renders the OpenCode theme sbox from the palette and selects it", async () => {
    // Arrange
    await install();

    // Act
    const result = await run(["tokyo-night"]);

    // Assert
    expect(result.stderr).toBe("");
    expect(result.code).toBe(0);
    const theme = await homeJson(".config/opencode/themes/sbox.json");
    expect(theme.$schema).toBe("https://opencode.ai/theme.json");
    expect(theme.theme.primary).toEqual({ dark: "#7aa2f7", light: "#7aa2f7" });
    expect(theme.theme.background).toEqual({ dark: "#1a1b26", light: "#1a1b26" });
    expect(theme.theme.backgroundPanel).toEqual({ dark: "#232431", light: "#232431" });
    expect(theme.theme.diffRemovedBg).toEqual({ dark: "#3b2936", light: "#3b2936" });
    expect(await homeJson(".config/opencode/cli.json")).toEqual({
      $schema: "https://opencode.ai/v2/cli.json",
      theme: { name: "sbox", mode: "dark" },
    });
  });
});

describe("sbox theme <name> for Claude Code", () => {
  test("renders the Claude Code theme sbox from the palette and selects it", async () => {
    // Arrange
    await install();

    // Act
    const result = await run(["tokyo-night"]);

    // Assert
    expect(result.code).toBe(0);
    const theme = await homeJson(".claude/themes/sbox.json");
    expect(theme.name).toBe("sbox");
    expect(theme.base).toBe("dark");
    expect(theme.overrides.claude).toBe("#7aa2f7");
    expect(theme.overrides.claudeShimmer).toBe("#8aa7eb");
    expect(theme.overrides.inactive).toBe("#707590");
    expect(theme.overrides.diffRemovedWord).toBe("#613847");
    expect(theme.overrides.selectionBg).toBe("#292e42");
    expect(await homeJson(".claude/settings.json")).toEqual({ theme: "custom:sbox" });
  });

  test("uses the light base for a light theme", async () => {
    // Arrange
    await install();

    // Act
    await run(["white"]);

    // Assert
    const theme = await homeJson(".claude/themes/sbox.json");
    expect(theme.base).toBe("light");
    expect(theme.overrides.text).toBe("#000000");
    expect(theme.overrides.diffAdded).toBe("#e1e1e1");
  });

  test("keeps the other settings of Claude Code's settings.json", async () => {
    // Arrange
    await install();
    await mkdir(join(home, ".claude"), { recursive: true });
    const settings = { model: "opus", permissions: { allow: ["Bash(ls:*)"] }, theme: "dark" };
    await writeFile(join(home, ".claude/settings.json"), JSON.stringify(settings, null, 2) + "\n");

    // Act
    await run(["nord"]);
    const first = await homeFile(".claude/settings.json");
    await run(["nord"]);

    // Assert
    expect(JSON.parse(first)).toEqual({ ...settings, theme: "custom:sbox" });
    expect(await homeFile(".claude/settings.json")).toBe(first);
  });
});

describe("sbox theme <name> for pi", () => {
  test("renders the pi theme sbox from the palette and selects it", async () => {
    // Arrange
    await install();

    // Act
    const result = await run(["tokyo-night"]);

    // Assert
    expect(result.code).toBe(0);
    const theme = await homeJson(".pi/agent/themes/sbox.json");
    expect(theme.name).toBe("sbox");
    expect(theme.vars.background).toBe("#1a1b26");
    expect(theme.vars.selectedBackground).toBe("#2f3954");
    expect(theme.vars.panel).toBe("#232431");
    expect(theme.vars.dimText).toBe("#5f637a");
    expect(theme.colors.userMessageBg).toBe("panel");
    expect(await homeJson(".pi/agent/settings.json")).toEqual({ theme: "sbox" });
  });

  test("uses light colors for a light theme", async () => {
    // Arrange
    await install();

    // Act
    await run(["white"]);

    // Assert
    const theme = await homeJson(".pi/agent/themes/sbox.json");
    expect(theme.vars.background).toBe("#ffffff");
    expect(theme.vars.panel).toBe("#f0f0f0");
  });

  test("keeps the other settings of pi's settings.json", async () => {
    // Arrange
    await install();
    await mkdir(join(home, ".pi/agent"), { recursive: true });
    const settings = { defaultProvider: "anthropic", packages: ["npm:pi-skills"], theme: "dark" };
    await writeFile(join(home, ".pi/agent/settings.json"), JSON.stringify(settings, null, 2) + "\n");

    // Act
    await run(["nord"]);
    const first = await homeFile(".pi/agent/settings.json");
    await run(["nord"]);

    // Assert
    expect(JSON.parse(first)).toEqual({ ...settings, theme: "sbox" });
    expect(await homeFile(".pi/agent/settings.json")).toBe(first);
  });
});

describe("sbox theme <name>, continued", () => {
  test("saves the choice and tells which apps to restart", async () => {
    // Arrange
    await install();

    // Act
    const result = await run(["nord"]);

    // Assert
    expect(result.code).toBe(0);
    expect(await homeFile(".local/state/sbox/theme/current")).toBe("nord\n");
    expect(result.stdout).toBe("Switched to the theme 'nord'. Restart Claude Code, OpenCode and pi to see it.\n");
  });

  test("selects the light mode for a light theme", async () => {
    // Arrange
    await install();

    // Act
    await run(["white"]);

    // Assert
    expect((await homeJson(".config/opencode/cli.json")).theme).toEqual({ name: "sbox", mode: "light" });
  });

  test("keeps the other settings of OpenCode's cli.json", async () => {
    // Arrange
    await install();
    await mkdir(join(home, ".config/opencode"), { recursive: true });
    await writeFile(
      join(home, ".config/opencode/cli.json"),
      JSON.stringify({ keybinds: { leader: "ctrl+x" }, theme: { name: "tokyo-night", mode: "light" } }),
    );

    // Act
    await run(["nord"]);
    await run(["nord"]);

    // Assert
    expect(await homeJson(".config/opencode/cli.json")).toEqual({
      keybinds: { leader: "ctrl+x" },
      theme: { name: "sbox", mode: "dark" },
      $schema: "https://opencode.ai/v2/cli.json",
    });
  });

  test("fails for an unknown theme, lists the themes and changes nothing", async () => {
    // Arrange
    await install();
    await save("nord");

    // Act
    const result = await run(["tokio-night"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stdout).toBe("");
    expect(result.stderr).toStartWith("sbox theme: there is no theme 'tokio-night'. The themes are:\n");
    expect(result.stderr).toContain("\n  osaka-jade\n");
    expect(result.stderr).toContain("\n  tokyo-night\n");
    expect(await homeFile(".local/state/sbox/theme/current")).toBe("nord\n");
    expect(await Bun.file(join(home, ".config/opencode/themes/sbox.json")).exists()).toBe(false);
  });
});

describe("sbox theme --list", () => {
  test("lists the themes with the current one marked", async () => {
    // Arrange
    await install();
    await save("nord");

    // Act
    const result = await run(["--list"]);

    // Assert
    expect(result.code).toBe(0);
    const lines = result.stdout.trimEnd().split("\n");
    expect(lines).toHaveLength(24);
    expect(lines).toContain("* nord");
    expect(lines).toContain("  tokyo-night");
    expect(lines.filter((line) => line.startsWith("*"))).toEqual(["* nord"]);
  });

  test("is what sbox theme does without a name", async () => {
    // Arrange
    await install();

    // Act
    const list = await run(["--list"]);
    const bare = await run([]);

    // Assert
    expect(bare).toEqual(list);
    expect(bare.stdout).toContain("* tokyo-night\n");
  });
});

describe("sbox theme --current and --apply", () => {
  test("use the saved choice first", async () => {
    // Arrange
    await install();
    await save("nord");

    // Act
    const current = await run(["--current"], { SBOX_THEME: "white" });
    const apply = await run(["--apply"], { SBOX_THEME: "white" });

    // Assert
    expect(current.stdout).toBe("nord\n");
    expect(apply.code).toBe(0);
    expect(apply.stdout).toBe("Applied the theme 'nord'.\n");
    expect((await homeJson(".config/opencode/themes/sbox.json")).theme.background.dark).toBe("#2e3440");
  });

  test("use the kit's default SBOX_THEME without a saved choice", async () => {
    // Arrange
    await install();

    // Act
    const current = await run(["--current"], { SBOX_THEME: "white" });
    const apply = await run(["--apply"], { SBOX_THEME: "white" });

    // Assert
    expect(current.stdout).toBe("white\n");
    expect(apply.stdout).toBe("Applied the theme 'white'.\n");
    expect((await homeJson(".config/opencode/cli.json")).theme.mode).toBe("light");
  });

  test("use tokyo-night without a saved choice or SBOX_THEME", async () => {
    // Arrange
    await install();

    // Act
    const current = await run(["--current"]);
    const apply = await run(["--apply"]);

    // Assert
    expect(current.stdout).toBe("tokyo-night\n");
    expect(apply.stdout).toBe("Applied the theme 'tokyo-night'.\n");
    expect((await homeJson(".config/opencode/themes/sbox.json")).theme.background.dark).toBe("#1a1b26");
  });

  test("warn about a saved theme that is missing, apply the fallback and keep the choice", async () => {
    // Arrange
    await install();
    await save("osaka-teal");

    // Act
    const apply = await run(["--apply"], { SBOX_THEME: "nord" });

    // Assert
    expect(apply.code).toBe(0);
    expect(apply.stderr).toBe("sbox theme: the saved theme 'osaka-teal' is not installed; using 'nord'.\n");
    expect(apply.stdout).toBe("Applied the theme 'nord'.\n");
    expect((await homeJson(".config/opencode/themes/sbox.json")).theme.background.dark).toBe("#2e3440");
    expect(await homeFile(".local/state/sbox/theme/current")).toBe("osaka-teal\n");
  });
});

const probe = `mode={{ mode }}
background={{ background }} dark={{ dark_background }} darker={{ darker_background }}
foreground={{ foreground }} accent={{ accent }} orange={{ orange }} brown={{ brown }}
bright_red={{ bright_red }} color9={{ color9 }} color0={{ color0 }}
mix={{ background | mix: foreground, 25 }} percent={{ red | mix: blue, "50%" }}
strip={{ bright_red | strip }} rgb={{ bright_red | rgb }}
{% if mode == "light" %}light{% else %}dark{% endif %}
`;

const dusk = ["background", "foreground", "red", "yellow", "blue"]
  .map((key, i) => `${key} = "#10101${i}"`)
  .join("\n");

function probeApp(extra = "") {
  return {
    "apps/probe/app.yaml": `files:\n  - template: probe.liquid\n    target: ~/probe.txt\n${extra}`,
    "apps/probe/probe.liquid": probe,
  };
}

describe("palettes and templates", () => {
  test("resolve legacy names, color aliases and derived colors like upstream", async () => {
    // Arrange
    await install({
      ...probeApp(),
      "themes/legacy/colors.toml": [
        'bg = "#ffffff"',
        'fg = "#000000"',
        'color1 = "#ff0000"',
        'color3 = "#00ff00"',
        'color4 = "#0000ff"',
      ].join("\n"),
    });

    // Act
    const result = await run(["legacy"]);

    // Assert
    expect(result.stderr).toBe("");
    expect(await homeFile("probe.txt")).toBe(`mode=light
background=#ffffff dark=#bfbfbf darker=#808080
foreground=#000000 accent=#0000ff orange=#00ff00 brown=#008000
bright_red=#ff3333 color9=#ff3333 color0=#ffffff
mix=#bfbfbf percent=#800080
strip=ff3333 rgb=255,51,51
light
`);
  });

  test("keep the colors a palette sets and its mode", async () => {
    // Arrange
    await install({
      ...probeApp(),
      "themes/set/colors.toml": [
        'mode = "dark"',
        'accent = "#123456"',
        'background = "#F0F0F0"',
        'dark_background = "#010101"',
        'darker_background = "#020202"',
        'foreground = "#0F0F0F"',
        'red = "#ff0000"',
        'blue = "#0000ff"',
        'orange = "#ff8000"',
        'brown = "#030303"',
        'bright_red = "#040404"',
      ].join("\n"),
    });

    // Act
    await run(["set"]);

    // Assert
    expect(await homeFile("probe.txt")).toBe(`mode=dark
background=#F0F0F0 dark=#010101 darker=#020202
foreground=#0F0F0F accent=#123456 orange=#ff8000 brown=#030303
bright_red=#040404 color9=#040404 color0=#F0F0F0
mix=#b8b8b8 percent=#800080
strip=040404 rgb=4,4,4
dark
`);
  });

  test("run the app's commands after rendering", async () => {
    // Arrange
    await install({ ...probeApp("run:\n  - fake-bat cache --build\n"), "themes/dusk/colors.toml": dusk });
    const bin = join(home, "bin");
    await mkdir(bin);
    await writeFile(
      join(bin, "fake-bat"),
      `#!/bin/sh\necho "$PWD $@ $(cat probe.txt | head -1)" > ran.txt\n`,
      { mode: 0o755 },
    );

    // Act
    const result = await run(["dusk"], { PATH: `${bin}:${process.env.PATH}` });

    // Assert
    expect(result.code).toBe(0);
    expect(await homeFile("ran.txt")).toBe(`${home} cache --build mode=dark\n`);
  });

  test("report a failing command and fail after rendering", async () => {
    // Arrange
    await install({ ...probeApp("run:\n  - echo broken >&2; exit 3\n"), "themes/dusk/colors.toml": dusk });

    // Act
    const result = await run(["dusk"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toBe(
      "sbox theme: probe: 'echo broken >&2; exit 3' failed with exit code 3:\nbroken\n",
    );
    expect(await homeFile("probe.txt")).toStartWith("mode=dark\n");
  });

  test("reject an app definition with an unknown key", async () => {
    // Arrange
    await install({ ...probeApp("restart: Probe\ncolours: []\n"), "themes/dusk/colors.toml": dusk });

    // Act
    const result = await run(["dusk"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toContain("probe/app.yaml: unknown key 'colours'");
  });

  test("name the template that uses a color the palette lacks", async () => {
    // Arrange
    await install({ ...probeApp(), "themes/bare/colors.toml": 'background = "#101010"\n' });

    // Act
    const result = await run(["bare"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toStartWith("sbox theme: probe/probe.liquid: undefined variable: foreground");
  });
});
