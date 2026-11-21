import { describe, expect, test } from "bun:test";
import { useThemeHome } from "./theme-home";

const home = useThemeHome();

const probe = `mode={{ mode }}
background={{ background }} dark={{ dark_background }} darker={{ darker_background }}
foreground={{ foreground }} accent={{ accent }} orange={{ orange }} brown={{ brown }}
bright_red={{ bright_red }} color9={{ color9 }} color0={{ color0 }}
mix={{ background | mix: foreground, 25 }} percent={{ red | mix: blue, "50%" }}
strip={{ bright_red | strip }} rgb={{ bright_red | rgb }}
{% if mode == "light" %}light{% else %}dark{% endif %}
`;

function probeApp(extra = "") {
  return {
    "apps/probe/app.yaml": `files:\n  - template: probe.liquid\n    target: ~/probe.txt\n${extra}`,
    "apps/probe/probe.liquid": probe,
  };
}

describe("palettes", () => {
  test("resolve legacy names, color aliases and derived colors like upstream", async () => {
    // Arrange
    await home.install({
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
    const result = await home.run(["legacy"]);

    // Assert
    expect(result.stderr).toBe("");
    expect(await home.file("probe.txt")).toBe(`mode=light
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
    await home.install({
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
    await home.run(["set"]);

    // Assert
    expect(await home.file("probe.txt")).toBe(`mode=dark
background=#F0F0F0 dark=#010101 darker=#020202
foreground=#0F0F0F accent=#123456 orange=#ff8000 brown=#030303
bright_red=#040404 color9=#040404 color0=#F0F0F0
mix=#b8b8b8 percent=#800080
strip=040404 rgb=4,4,4
dark
`);
  });
});
