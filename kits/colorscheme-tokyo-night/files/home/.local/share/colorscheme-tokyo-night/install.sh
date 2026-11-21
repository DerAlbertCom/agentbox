#!/bin/sh
set -eu

# bat and delta read custom themes only from bat's cache; the agents' settings are owned by other kits.
export HOME="${HOME:-/home/agent}"
export PATH="$HOME/.local/share/mise/shims:$PATH"

bat cache --build >/dev/null

node - <<'JS'
const fs = require("fs");
const path = require("path");
const home = process.env.HOME;
const selections = [
  [path.join(home, ".claude/settings.json"), "custom:tokyo-night"],
  [path.join(home, ".pi/agent/settings.json"), "tokyo-night"],
];
for (const [file, theme] of selections) {
  const settings = fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, "utf8")) : {};
  settings.theme = theme;
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, JSON.stringify(settings, null, 2) + "\n");
}
JS
