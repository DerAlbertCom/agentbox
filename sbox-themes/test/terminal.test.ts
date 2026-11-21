import { describe, expect, test } from "bun:test";
import { join } from "node:path";
import { useThemeHome } from "./theme-home";

const home = useThemeHome();

const colorScript = ".config/sbox/terminal-colors.sh";

/** The OSC sequences that set the 16 ANSI colors, then the foreground, background and cursor. */
function sequences(ansi: string[], foreground: string, background: string, cursor: string): string {
  return (
    ansi.map((color, i) => `\x1b]4;${i};${color}\x07`).join("") +
    `\x1b]10;${foreground}\x07\x1b]11;${background}\x07\x1b]12;${cursor}\x07`
  );
}

const tokyoNight = sequences(
  [
    "#1a1b26", "#f7768e", "#9ece6a", "#e0af68", "#7aa2f7", "#ad8ee6", "#449dab", "#a9b1d6",
    "#414868", "#ff7a93", "#b9f27c", "#ff9e64", "#7da6ff", "#bb9af7", "#0db9d7", "#c0caf5",
  ],
  "#a9b1d6",
  "#1a1b26",
  "#c0caf5",
);

const white = sequences(
  [
    "#ffffff", "#2a2a2a", "#3a3a3a", "#4a4a4a", "#1a1a1a", "#2e2e2e", "#3e3e3e", "#000000",
    "#808080", "#2a2a2a", "#3a3a3a", "#4a4a4a", "#1a1a1a", "#2e2e2e", "#3e3e3e", "#000000",
  ],
  "#000000",
  "#ffffff",
  "#000000",
);

/** Runs the rendered terminal color script the way the login script does and returns its output. */
function runColorScript(): string {
  const result = Bun.spawnSync(["sh", join(home.path, colorScript)], { stdout: "pipe", stderr: "pipe" });
  expect(result.stderr.toString()).toBe("");
  expect(result.exitCode).toBe(0);
  return result.stdout.toString();
}

describe("the terminal color script", () => {
  test("prints the colors of a dark theme", async () => {
    // Arrange
    await home.install();

    // Act
    await home.run(["tokyo-night"]);

    // Assert
    expect(runColorScript()).toBe(tokyoNight);
  });

  test("prints the colors of a light theme", async () => {
    // Arrange
    await home.install();

    // Act
    await home.run(["white"]);

    // Assert
    expect(runColorScript()).toBe(white);
  });

  test("is rendered by --apply for the saved choice, then SBOX_THEME, then tokyo-night", async () => {
    // Arrange
    await home.install();

    // Act
    await home.run(["--apply"]);
    const fallback = runColorScript();
    await home.run(["--apply"], { SBOX_THEME: "white" });
    const preset = runColorScript();
    await home.save("tokyo-night");
    await home.run(["--apply"], { SBOX_THEME: "white" });
    const saved = runColorScript();

    // Assert
    expect(fallback).toBe(tokyoNight);
    expect(preset).toBe(white);
    expect(saved).toBe(tokyoNight);
  });
});

const terminalTheme = { SBOX_TERMINAL_THEME: "1" };
const switched = (name: string) => `Switched to the theme '${name}'. Some apps show it only after a restart.\n`;

describe("sbox theme <name> and the terminal colors", () => {
  test("recolors the terminal with SBOX_TERMINAL_THEME=1", async () => {
    // Arrange
    await home.install();

    // Act
    const result = await home.run(["white"], terminalTheme, { tty: true });

    // Assert
    expect(result.stderr).toBe("");
    expect(result.code).toBe(0);
    expect(result.stdout).toBe(white + switched("white"));
  });

  test("leaves the terminal alone without SBOX_TERMINAL_THEME=1", async () => {
    // Arrange
    await home.install();

    // Act
    const unset = await home.run(["white"], {}, { tty: true });
    const off = await home.run(["white"], { SBOX_TERMINAL_THEME: "0" }, { tty: true });

    // Assert
    expect(unset.stdout).toBe(switched("white"));
    expect(off.stdout).toBe(switched("white"));
  });

  test("leaves the output alone when it is not a terminal", async () => {
    // Arrange
    await home.install();

    // Act
    const result = await home.run(["white"], terminalTheme);

    // Assert
    expect(result.stdout).toBe(switched("white"));
    expect(runColorScript()).toBe(white);
  });

  test("switches the theme without colors when the terminal color script is missing", async () => {
    // Arrange
    await home.install({
      "themes/white/colors.toml": 'background = "#ffffff"\nforeground = "#000000"\n',
    });

    // Act
    const result = await home.run(["white"], terminalTheme, { tty: true });

    // Assert
    expect(result.stderr).toBe("");
    expect(result.code).toBe(0);
    expect(result.stdout).toBe(switched("white"));
  });
});

describe("sbox theme --apply, --list, --current and --preview", () => {
  test("never recolor the terminal", async () => {
    // Arrange
    await home.install();
    await home.save("white");

    // Act
    const apply = await home.run(["--apply"], terminalTheme, { tty: true });
    const list = await home.run(["--list"], terminalTheme, { tty: true });
    const current = await home.run(["--current"], terminalTheme, { tty: true });
    const preview = await home.run(["--preview", "white"], terminalTheme, { tty: true });

    // Assert
    expect(apply.stdout).toBe("Applied the theme 'white'.\n");
    expect(runColorScript()).toBe(white);
    for (const result of [list, current, preview]) {
      expect(result.code).toBe(0);
      expect(result.stdout).not.toContain("\x1b]");
    }
  });
});
