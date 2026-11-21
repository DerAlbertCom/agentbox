// Refreshes the palettes in ../themes from upstream: `bun run sync-themes` in this package.
// Adds and updates themes, never deletes; review the diff before committing.
import { join } from "node:path";
import { syncThemes, type ThemeSource } from "./sync";

const sources: ThemeSource[] = [
  { repo: "basecamp/omarchy", ref: "quattro" },
  // Moved out of the collection into its own repository, with its own license.
  { repo: "omacom/omarchy-osaka-jade-theme", ref: "main", theme: "osaka-jade" },
];

const report = await syncThemes({ themes: join(import.meta.dir, "../themes"), sources, fetch });
const line = (label: string, names: string[]) =>
  console.log(`${label}: ${names.length ? names.join(", ") : "none"}`);
line("Added", report.added);
line("Updated", report.updated);
console.log(`Unchanged: ${report.unchanged.length} themes`);
line("Removed upstream, kept here", report.removed);
