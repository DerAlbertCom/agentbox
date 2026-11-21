import { existsSync, readdirSync } from "node:fs";
import { join } from "node:path";

/** The sorted names of the folders in `folder` that hold `file`; none when `folder` is missing. */
export function foldersWith(folder: string, file: string): string[] {
  if (!existsSync(folder)) return [];
  return readdirSync(folder, { withFileTypes: true })
    .filter((entry) => entry.isDirectory() && existsSync(join(folder, entry.name, file)))
    .map((entry) => entry.name)
    .sort();
}
