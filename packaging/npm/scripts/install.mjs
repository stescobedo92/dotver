import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import https from "node:https";

const packageRoot = path.resolve(import.meta.dirname, "..");
const packageJson = JSON.parse(fs.readFileSync(path.join(packageRoot, "package.json"), "utf8"));
const vendorDir = path.join(packageRoot, "vendor");
const version = packageJson.version;
const repositoryUrl = packageJson.repository.url.replace(/\.git$/, "");

const platformMap = {
  win32: "win-x64.exe",
  linux: "linux-x64",
  darwin: os.arch() === "arm64" ? "osx-arm64" : "osx-x64"
};

const assetSuffix = platformMap[process.platform];
if (!assetSuffix) {
  console.warn(`dotver: unsupported platform ${process.platform}.`);
  process.exit(0);
}

const assetName = `dotver-${assetSuffix}`;
const downloadUrl = `${repositoryUrl}/releases/download/v${version}/${assetName}`;
const targetName = process.platform === "win32" ? "dotver.exe" : "dotver";
const targetPath = path.join(vendorDir, targetName);

fs.mkdirSync(vendorDir, { recursive: true });

await download(downloadUrl, targetPath);

if (process.platform !== "win32") {
  fs.chmodSync(targetPath, 0o755);
}

function download(url, destination) {
  return new Promise((resolve, reject) => {
    const file = fs.createWriteStream(destination);
    https.get(url, response => {
      if (response.statusCode && response.statusCode >= 300 && response.statusCode < 400 && response.headers.location) {
        file.close();
        fs.rmSync(destination, { force: true });
        download(response.headers.location, destination).then(resolve).catch(reject);
        return;
      }

      if (response.statusCode !== 200) {
        reject(new Error(`Download failed: ${response.statusCode} ${response.statusMessage}`));
        return;
      }

      response.pipe(file);
      file.on("finish", () => {
        file.close();
        resolve();
      });
    }).on("error", error => {
      file.close();
      fs.rmSync(destination, { force: true });
      reject(error);
    });
  });
}
