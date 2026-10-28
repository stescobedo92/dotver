#!/usr/bin/env node
const { spawnSync } = require("node:child_process");
const { existsSync } = require("node:fs");
const path = require("node:path");

const binName = process.platform === "win32" ? "dotver.exe" : "dotver";
const binary = path.join(__dirname, "..", "vendor", binName);

if (!existsSync(binary)) {
  console.error("dotver binary is missing. Reinstall the package or run npm rebuild.");
  process.exit(1);
}

const result = spawnSync(binary, ["intercept", "--", ...process.argv.slice(2)], { stdio: "inherit" });
process.exit(result.status ?? 1);
