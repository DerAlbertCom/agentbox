import { afterEach, beforeEach, describe, expect, test } from "bun:test";
import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";

const entry = join(import.meta.dir, "../src/main.ts");

let home: string;

beforeEach(async () => {
  home = await mkdtemp(join(tmpdir(), "sbox-main-"));
});

afterEach(async () => {
  await rm(home, { recursive: true, force: true });
});

describe("sbox on a pipe", () => {
  test("stops quietly when the reader closes the pipe early", async () => {
    // Arrange
    const command = join(home, ".local/share/sbox/commands/many");
    await mkdir(command, { recursive: true });
    await writeFile(
      join(command, "command.js"),
      `export default {
  summary: "Prints many lines",
  run(args, ctx) {
    for (let i = 0; i < 100000; i++) ctx.stdout.write("line " + i + "\\n");
  },
};
`,
    );

    // Act
    const result = Bun.spawnSync(["sh", "-c", `"$BUN" "$ENTRY" many | head -n 1`], {
      env: { HOME: home, PATH: process.env.PATH, BUN: process.execPath, ENTRY: entry },
    });

    // Assert
    expect(result.stdout.toString()).toBe("line 0\n");
    expect(result.stderr.toString()).toBe("");
  });
});
