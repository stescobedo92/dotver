import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { assetNameFor, downloadVerified, expectedChecksumFor } from "./lib.mjs";

const packageRoot = path.resolve(import.meta.dirname, "..");
const packageJson = JSON.parse(fs.readFileSync(path.join(packageRoot, "package.json"), "utf8"));

const assetName = assetNameFor(process.platform, os.arch());
if (!assetName) {
  console.warn(`dotver: unsupported platform ${process.platform}/${os.arch()}.`);
  process.exit(0);
}

const repositoryUrl = packageJson.repository.url.replace(/\.git$/, "");
const downloadUrl = `${repositoryUrl}/releases/download/v${packageJson.version}/${assetName}`;
const vendorDir = path.join(packageRoot, "vendor");
const targetPath = path.join(vendorDir, process.platform === "win32" ? "dotver.exe" : "dotver");

try {
  // The digest ships inside the immutable npm tarball, so a release asset
  // swapped after publishing can no longer become this machine's dotnet.
  const expectedSha256 = expectedChecksumFor(packageJson, assetName);
  fs.mkdirSync(vendorDir, { recursive: true });
  await downloadVerified({ url: downloadUrl, destination: targetPath, expectedSha256 });
  if (process.platform !== "win32") {
    fs.chmodSync(targetPath, 0o755);
  }
} catch (error) {
  console.error(error instanceof Error ? error.message : String(error));
  process.exit(1);
}
