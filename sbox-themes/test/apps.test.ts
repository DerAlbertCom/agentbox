import { describe, expect, test } from "bun:test";
import { mkdir, writeFile } from "node:fs/promises";
import { join } from "node:path";
import { useThemeHome } from "./theme-home";

const home = useThemeHome();

describe("sbox theme <name> for OpenCode", () => {
  test("renders the OpenCode theme sbox from the palette and selects it", async () => {
    // Arrange
    await home.install();

    // Act
    const result = await home.run(["tokyo-night"]);

    // Assert
    expect(result.stderr).toBe("");
    expect(result.code).toBe(0);
    const theme = await home.json(".config/opencode/themes/sbox.json");
    expect(theme.$schema).toBe("https://opencode.ai/theme.json");
    expect(theme.theme.primary).toEqual({ dark: "#7aa2f7", light: "#7aa2f7" });
    expect(theme.theme.background).toEqual({ dark: "#1a1b26", light: "#1a1b26" });
    expect(theme.theme.backgroundPanel).toEqual({ dark: "#232431", light: "#232431" });
    expect(theme.theme.diffRemovedBg).toEqual({ dark: "#3b2936", light: "#3b2936" });
    expect(await home.json(".config/opencode/cli.json")).toEqual({
      $schema: "https://opencode.ai/v2/cli.json",
      theme: { name: "sbox", mode: "dark" },
    });
  });

  test("selects the light mode for a light theme", async () => {
    // Arrange
    await home.install();

    // Act
    await home.run(["white"]);

    // Assert
    expect((await home.json(".config/opencode/cli.json")).theme).toEqual({ name: "sbox", mode: "light" });
  });

  test("keeps the other settings of OpenCode's cli.json", async () => {
    // Arrange
    await home.install();
    await home.write(
      ".config/opencode/cli.json",
      JSON.stringify({ keybinds: { leader: "ctrl+x" }, theme: { name: "tokyo-night", mode: "light" } }),
    );

    // Act
    await home.run(["nord"]);
    await home.run(["nord"]);

    // Assert
    expect(await home.json(".config/opencode/cli.json")).toEqual({
      keybinds: { leader: "ctrl+x" },
      theme: { name: "sbox", mode: "dark" },
      $schema: "https://opencode.ai/v2/cli.json",
    });
  });
});

describe("sbox theme <name> for Claude Code", () => {
  test("renders the Claude Code theme sbox from the palette and selects it", async () => {
    // Arrange
    await home.install();

    // Act
    const result = await home.run(["tokyo-night"]);

    // Assert
    expect(result.code).toBe(0);
    const theme = await home.json(".claude/themes/sbox.json");
    expect(theme.name).toBe("sbox");
    expect(theme.base).toBe("dark");
    expect(theme.overrides.claude).toBe("#7aa2f7");
    expect(theme.overrides.claudeShimmer).toBe("#8aa7eb");
    expect(theme.overrides.inactive).toBe("#707590");
    expect(theme.overrides.diffRemovedWord).toBe("#613847");
    expect(theme.overrides.selectionBg).toBe("#292e42");
    expect(await home.json(".claude/settings.json")).toEqual({ theme: "custom:sbox" });
  });

  test("uses the light base for a light theme", async () => {
    // Arrange
    await home.install();

    // Act
    await home.run(["white"]);

    // Assert
    const theme = await home.json(".claude/themes/sbox.json");
    expect(theme.base).toBe("light");
    expect(theme.overrides.text).toBe("#000000");
    expect(theme.overrides.diffAdded).toBe("#e1e1e1");
  });

  test("keeps the other settings of Claude Code's settings.json", async () => {
    // Arrange
    await home.install();
    const settings = { model: "opus", permissions: { allow: ["Bash(ls:*)"] }, theme: "dark" };
    await home.write(".claude/settings.json", JSON.stringify(settings, null, 2) + "\n");

    // Act
    await home.run(["nord"]);
    const first = await home.file(".claude/settings.json");
    await home.run(["nord"]);

    // Assert
    expect(JSON.parse(first)).toEqual({ ...settings, theme: "custom:sbox" });
    expect(await home.file(".claude/settings.json")).toBe(first);
  });
});


describe("sbox theme <name> for pi", () => {
  test("renders the pi theme sbox from the palette and selects it", async () => {
    // Arrange
    await home.install();

    // Act
    const result = await home.run(["tokyo-night"]);

    // Assert
    expect(result.code).toBe(0);
    const theme = await home.json(".pi/agent/themes/sbox.json");
    expect(theme.name).toBe("sbox");
    expect(theme.vars.background).toBe("#1a1b26");
    expect(theme.vars.selectedBackground).toBe("#2f3954");
    expect(theme.vars.panel).toBe("#232431");
    expect(theme.vars.dimText).toBe("#5f637a");
    expect(theme.colors.userMessageBg).toBe("panel");
    expect(await home.json(".pi/agent/settings.json")).toEqual({ theme: "sbox" });
  });

  test("uses light colors for a light theme", async () => {
    // Arrange
    await home.install();

    // Act
    await home.run(["white"]);

    // Assert
    const theme = await home.json(".pi/agent/themes/sbox.json");
    expect(theme.vars.background).toBe("#ffffff");
    expect(theme.vars.panel).toBe("#f0f0f0");
  });

  test("keeps the other settings of pi's settings.json", async () => {
    // Arrange
    await home.install();
    const settings = { defaultProvider: "anthropic", packages: ["npm:pi-skills"], theme: "dark" };
    await home.write(".pi/agent/settings.json", JSON.stringify(settings, null, 2) + "\n");

    // Act
    await home.run(["nord"]);
    const first = await home.file(".pi/agent/settings.json");
    await home.run(["nord"]);

    // Assert
    expect(JSON.parse(first)).toEqual({ ...settings, theme: "sbox" });
    expect(await home.file(".pi/agent/settings.json")).toBe(first);
  });
});


describe("sbox theme <name> for the terminal tools", () => {
  test("renders the bat theme sbox, selects it and rebuilds bat's cache with it", async () => {
    // Arrange
    await home.install();

    // Act
    const result = await home.run(["nord"]);

    // Assert
    expect(result.code).toBe(0);
    const theme = await home.file(".config/bat/themes/sbox.tmTheme");
    expect(theme).toContain("<key>name</key>\n\t<string>sbox</string>");
    expect(theme).toContain("<key>background</key>\n\t\t\t\t<string>#2e3440</string>");
    expect(theme).toContain("<key>lineHighlight</key>\n\t\t\t\t<string>#383e4a</string>");
    expect(theme).toContain("<key>gutterForeground</key>\n\t\t\t\t<string>#616773</string>");
    expect(await home.file(".config/bat/config")).toBe("--theme=sbox\n");
    expect(await home.file("bat.log")).toBe(`${home.path}: bat cache --build\n`);
    expect(await home.file("bat.cache")).toBe(theme);
  });

  test("renders delta's git config with the bat theme sbox and the palette's diff colors", async () => {
    // Arrange
    await home.install();

    // Act
    await home.run(["nord"]);

    // Assert
    const config = await home.file(".config/git/config");
    expect(config).toStartWith("[delta]\n\tsyntax-theme = sbox\n");
    expect(config).toContain('\tminus-style = syntax "#443b46"\n');
    expect(config).toContain('\tminus-emph-style = syntax "#5c424d"\n');
    expect(config).toContain('\tplus-style = syntax "#40494b"\n');
    expect(config).toContain('\tplus-emph-style = syntax "#536058"\n');
    expect(config).toContain('\tline-numbers-minus-style = "#bf616a"\n');
    expect(config).toContain('\tline-numbers-zero-style = "#616773"\n');
  });

  test("renders fzf's options file with the palette's colors", async () => {
    // Arrange
    await home.install();

    // Act
    await home.run(["nord"]);

    // Assert
    expect(await home.file(".config/fzf/sbox")).toBe(
      [
        "--color=fg:#d8dee9,bg:-1,hl:#81a1c1,fg+:#d8dee9,bg+:#434c5e,hl+:#81a1c1",
        "--color=info:#949aa5,prompt:#88c0d0,pointer:#b48ead,marker:#a3be8c",
        "--color=spinner:#b48ead,header:#ebcb8b,border:#616773,gutter:-1",
        "",
      ].join("\n"),
    );
  });

  test("renders lazygit's theme config with the palette's colors", async () => {
    // Arrange
    await home.install();

    // Act
    await home.run(["nord"]);

    // Assert
    const theme = Bun.YAML.parse(await home.file(".config/lazygit/config.yml")) as {
      gui: { theme: Record<string, string[]> };
    };
    expect(theme.gui.theme.activeBorderColor).toEqual(["#81a1c1", "bold"]);
    expect(theme.gui.theme.inactiveBorderColor).toEqual(["#616773"]);
    expect(theme.gui.theme.searchingActiveBorderColor).toEqual(["#d5967a", "bold"]);
    expect(theme.gui.theme.selectedLineBgColor).toEqual(["#434c5e"]);
    expect(theme.gui.theme.inactiveViewSelectedLineBgColor).toEqual(["#3f4551"]);
    expect(theme.gui.theme.defaultFgColor).toEqual(["#d8dee9"]);
  });

  test("renders yazi's theme with the palette's colors", async () => {
    // Arrange
    await home.install();

    // Act
    await home.run(["nord"]);

    // Assert
    const theme = Bun.TOML.parse(await home.file(".config/yazi/theme.toml")) as Record<
      string,
      Record<string, unknown>
    >;
    expect(theme.mgr!.cwd).toEqual({ fg: "#88c0d0", italic: true });
    expect(theme.mgr!.find_position).toEqual({ fg: "#8fbcbb", bold: true });
    expect(theme.tabs!.inactive).toEqual({ fg: "#81a1c1", bg: "#3f4551" });
    expect(theme.status!.overall).toEqual({ fg: "#d8dee9", bg: "#383e4a" });
    expect(theme.filetype!.rules).toContainEqual({ url: "*/", fg: "#81a1c1" });
  });
});


const probe = "mode={{ mode }}\nforeground={{ foreground }}\n";

const dusk = ["background", "foreground", "red", "yellow", "blue"]
  .map((key, i) => `${key} = "#10101${i}"`)
  .join("\n");

function probeApp(extra = "") {
  return {
    "apps/probe/app.yaml": `files:\n  - template: probe.liquid\n    target: ~/probe.txt\n${extra}`,
    "apps/probe/probe.liquid": probe,
  };
}

describe("app definitions", () => {
  test("run the app's commands after rendering", async () => {
    // Arrange
    await home.install({ ...probeApp("run:\n  - fake-bat cache --build\n"), "themes/dusk/colors.toml": dusk });
    const bin = join(home.path, "bin");
    await mkdir(bin);
    await writeFile(
      join(bin, "fake-bat"),
      `#!/bin/sh\necho "$PWD $@ $(cat probe.txt | head -1)" > ran.txt\n`,
      { mode: 0o755 },
    );

    // Act
    const result = await home.run(["dusk"], { PATH: `${bin}:${process.env.PATH}` });

    // Assert
    expect(result.code).toBe(0);
    expect(await home.file("ran.txt")).toBe(`${home.path} cache --build mode=dark\n`);
  });

  test("report a failing command and fail after rendering", async () => {
    // Arrange
    await home.install({ ...probeApp("run:\n  - echo broken >&2; exit 3\n"), "themes/dusk/colors.toml": dusk });

    // Act
    const result = await home.run(["dusk"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toBe(
      "sbox theme: probe: 'echo broken >&2; exit 3' failed with exit code 3:\nbroken\n",
    );
    expect(await home.file("probe.txt")).toStartWith("mode=dark\n");
  });

  test("reject an app definition with an unknown key", async () => {
    // Arrange
    await home.install({ ...probeApp("restart: Probe\ncolours: []\n"), "themes/dusk/colors.toml": dusk });

    // Act
    const result = await home.run(["dusk"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toContain("probe/app.yaml: unknown key 'colours'");
  });

  test("name the template that uses a color the palette lacks", async () => {
    // Arrange
    await home.install({ ...probeApp(), "themes/bare/colors.toml": 'background = "#101010"\n' });

    // Act
    const result = await home.run(["bare"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stderr).toStartWith("sbox theme: probe/probe.liquid: undefined variable: foreground");
  });
});
