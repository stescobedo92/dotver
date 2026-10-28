import crypto from "node:crypto";
import fs from "node:fs";
import https from "node:https";
import path from "node:path";

const redirectStatuses = new Set([301, 302, 303, 307, 308]);

export function assetNameFor(platform, arch) {
  switch (platform) {
    case "win32":
      return "dotver-win-x64.exe";
    case "linux":
      return "dotver-linux-x64";
    case "darwin":
      return arch === "arm64" ? "dotver-osx-arm64" : "dotver-osx-x64";
    default:
      return null;
  }
}

export function expectedChecksumFor(packageJson, assetName) {
  const table = packageJson.binaryChecksums;
  const value = table && typeof table === "object" ? table[assetName] : undefined;
  if (typeof value !== "string" || !/^[0-9a-f]{64}$/i.test(value)) {
    throw new Error(`dotver: package.json carries no SHA-256 for ${assetName}; refusing to install an unverifiable binary.`);
  }
  return value.toLowerCase();
}

/**
 * Downloads `url` into `destination` and keeps it only when its SHA-256 equals
 * `expectedSha256`. Redirects are bounded and restricted to `allowedProtocols`.
 * `get` is injectable so tests can use a local http server.
 */
export async function downloadVerified({
  url,
  destination,
  expectedSha256,
  get = https.get,
  allowedProtocols = ["https:"],
  maxRedirects = 5
}) {
  let current = new URL(url);
  for (let hop = 0; ; hop++) {
    if (!allowedProtocols.includes(current.protocol)) {
      throw new Error(`dotver: refusing to download from ${current} (protocol not allowed).`);
    }

    const response = await request(get, current);
    if (redirectStatuses.has(response.statusCode)) {
      response.resume();
      if (hop >= maxRedirects) {
        throw new Error(`dotver: too many redirects while downloading ${url}.`);
      }

      const location = response.headers.location;
      if (!location) {
        throw new Error(`dotver: redirect without a Location header from ${current}.`);
      }

      current = new URL(location, current);
      continue;
    }

    if (response.statusCode !== 200) {
      response.resume();
      throw new Error(`dotver: download failed: ${response.statusCode} ${response.statusMessage ?? ""}`.trim());
    }

    await writeVerified(response, destination, expectedSha256);
    return;
  }
}

function request(get, url) {
  return new Promise((resolve, reject) => {
    const outgoing = get(url, resolve);
    outgoing.on("error", reject);
  });
}

async function writeVerified(response, destination, expectedSha256) {
  // Hash while streaming into a temp file next to the destination, then
  // rename: the final path only ever holds a verified binary.
  const suffix = `${process.pid}.${crypto.randomBytes(4).toString("hex")}.partial`;
  const temp = path.join(path.dirname(destination), `.${path.basename(destination)}.${suffix}`);
  const hash = crypto.createHash("sha256");
  const file = fs.createWriteStream(temp, { flags: "wx" });
  try {
    await new Promise((resolve, reject) => {
      response.on("data", chunk => hash.update(chunk));
      response.on("error", reject);
      file.on("error", reject);
      file.on("finish", resolve);
      response.pipe(file);
    });

    const actual = hash.digest("hex");
    if (actual !== expectedSha256.toLowerCase()) {
      throw new Error(
        `dotver: checksum mismatch for ${path.basename(destination)}: expected ${expectedSha256}, got ${actual}. The download was discarded.`);
    }

    fs.renameSync(temp, destination);
  } catch (error) {
    fs.rmSync(temp, { force: true });
    throw error;
  }
}
