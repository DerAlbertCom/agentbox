import { afterEach, beforeEach, describe, expect, test } from "bun:test";
import { mkdir, mkdtemp, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";

const launcher = join(import.meta.dir, "../launcher/sbox");
const entry = join(import.meta.dir, "../src/main.ts");

let home: string;

beforeEach(async () => {
  home = await mkdtemp(join(tmpdir(), "sbox-launcher-"));
});

afterEach(async () => {
  await rm(home, { recursive: true, force: true });
});

function launch(path: string, ...args: string[]) {
  const result = Bun.spawnSync(["/bin/sh", launcher, ...args], { env: { HOME: home, PATH: path } });
  return { code: result.exitCode, stdout: result.stdout.toString(), stderr: result.stderr.toString() };
}

describe("the sbox launcher", () => {
  test("runs the bundled core with Bun", async () => {
    // Arrange
    const built = await Bun.build({
      entrypoints: [entry],
      target: "bun",
      outdir: join(home, ".local/share/sbox"),
      naming: "sbox.js",
    });
    expect(built.success).toBe(true);

    // Act
    const result = launch(dirname(process.execPath), "--help");

    // Assert
    expect(result.stderr).toBe("");
    expect(result.code).toBe(0);
    expect(result.stdout).toContain("No commands are installed.");
  });

  test("explains that Bun is missing", async () => {
    // Arrange
    const emptyPath = join(home, "empty-path");
    await mkdir(emptyPath);

    // Act
    const result = launch(emptyPath, "--help");

    // Assert
    expect(result.code).toBe(127);
    expect(result.stderr).toContain("sbox: the Bun runtime was not found");
  });
});
