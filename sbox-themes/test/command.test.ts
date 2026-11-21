import { describe, expect, test } from "bun:test";
import { useThemeHome } from "./theme-home";

const home = useThemeHome();

describe("sbox theme <name>", () => {
  test("saves the choice and tells that some apps show it only after a restart", async () => {
    // Arrange
    await home.install();

    // Act
    const result = await home.run(["nord"]);

    // Assert
    expect(result.code).toBe(0);
    expect(await home.file(".local/state/sbox/theme/current")).toBe("nord\n");
    expect(result.stdout).toBe("Switched to the theme 'nord'. Some apps show it only after a restart.\n");
  });

  test("fails for an unknown theme, lists the themes and changes nothing", async () => {
    // Arrange
    await home.install();
    await home.save("nord");

    // Act
    const result = await home.run(["tokio-night"]);

    // Assert
    expect(result.code).toBe(1);
    expect(result.stdout).toBe("");
    expect(result.stderr).toStartWith("sbox theme: there is no theme 'tokio-night'. The themes are:\n");
    expect(result.stderr).toContain("\n  osaka-jade\n");
    expect(result.stderr).toContain("\n  tokyo-night\n");
    expect(await home.file(".local/state/sbox/theme/current")).toBe("nord\n");
    expect(await home.exists(".config/opencode/themes/sbox.json")).toBe(false);
  });
});

describe("sbox theme --list", () => {
  test("lists the themes with the current one marked", async () => {
    // Arrange
    await home.install();
    await home.save("nord");

    // Act
    const result = await home.run(["--list"]);

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
    await home.install();

    // Act
    const list = await home.run(["--list"]);
    const bare = await home.run([]);

    // Assert
    expect(bare).toEqual(list);
    expect(bare.stdout).toContain("* tokyo-night\n");
  });
});


describe("sbox theme --current and --apply", () => {
  test("use the saved choice first", async () => {
    // Arrange
    await home.install();
    await home.save("nord");

    // Act
    const current = await home.run(["--current"], { SBOX_THEME: "white" });
    const apply = await home.run(["--apply"], { SBOX_THEME: "white" });

    // Assert
    expect(current.stdout).toBe("nord\n");
    expect(apply.code).toBe(0);
    expect(apply.stdout).toBe("Applied the theme 'nord'.\n");
    expect((await home.json(".config/opencode/themes/sbox.json")).theme.background.dark).toBe("#2e3440");
  });

  test("use the kit's default SBOX_THEME without a saved choice", async () => {
    // Arrange
    await home.install();

    // Act
    const current = await home.run(["--current"], { SBOX_THEME: "white" });
    const apply = await home.run(["--apply"], { SBOX_THEME: "white" });

    // Assert
    expect(current.stdout).toBe("white\n");
    expect(apply.stdout).toBe("Applied the theme 'white'.\n");
    expect((await home.json(".config/opencode/cli.json")).theme.mode).toBe("light");
  });

  test("use tokyo-night without a saved choice or SBOX_THEME", async () => {
    // Arrange
    await home.install();

    // Act
    const current = await home.run(["--current"]);
    const apply = await home.run(["--apply"]);

    // Assert
    expect(current.stdout).toBe("tokyo-night\n");
    expect(apply.stdout).toBe("Applied the theme 'tokyo-night'.\n");
    expect((await home.json(".config/opencode/themes/sbox.json")).theme.background.dark).toBe("#1a1b26");
  });

  test("warn about an SBOX_THEME that is missing and apply tokyo-night", async () => {
    // Arrange
    await home.install();

    // Act
    const apply = await home.run(["--apply"], { SBOX_THEME: "tokio-night" });

    // Assert
    expect(apply.code).toBe(0);
    expect(apply.stderr).toBe(
      "sbox theme: the default theme 'tokio-night' (SBOX_THEME) is not installed; using 'tokyo-night'.\n",
    );
    expect(apply.stdout).toBe("Applied the theme 'tokyo-night'.\n");
  });

  test("warn about a saved theme that is missing, apply the fallback and keep the choice", async () => {
    // Arrange
    await home.install();
    await home.save("osaka-teal");

    // Act
    const apply = await home.run(["--apply"], { SBOX_THEME: "nord" });

    // Assert
    expect(apply.code).toBe(0);
    expect(apply.stderr).toBe("sbox theme: the saved theme 'osaka-teal' is not installed; using 'nord'.\n");
    expect(apply.stdout).toBe("Applied the theme 'nord'.\n");
    expect((await home.json(".config/opencode/themes/sbox.json")).theme.background.dark).toBe("#2e3440");
    expect(await home.file(".local/state/sbox/theme/current")).toBe("osaka-teal\n");
  });
});

