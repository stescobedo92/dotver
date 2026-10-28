import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import http from "node:http";
import os from "node:os";
import path from "node:path";
import { after, before, test } from "node:test";
import { assetNameFor, downloadVerified, expectedChecksumFor } from "../scripts/lib.mjs";

const body = Buffer.from("#!/bin/sh\necho dotver\n");
const sha256 = crypto.createHash("sha256").update(body).digest("hex");

let server;
let baseUrl;
let tempDir;

before(async () => {
  tempDir = fs.mkdtempSync(path.join(os.tmpdir(), "dotver-npm-test-"));
  server = http.createServer((request, response) => {
    switch (request.url) {
      case "/asset":
        response.writeHead(200);
        response.end(body);
        return;
      case "/redirect":
        response.writeHead(302, { location: "/asset" });
        response.end();
        return;
      case "/loop":
        response.writeHead(302, { location: "/loop" });
        response.end();
        return;
      case "/elsewhere":
        response.writeHead(302, { location: "https://127.0.0.1:9/asset" });
        response.end();
        return;
      default:
        response.writeHead(404);
        response.end();
    }
  });
  await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
  baseUrl = `http://127.0.0.1:${server.address().port}`;
});

after(() => {
  server.close();
  fs.rmSync(tempDir, { recursive: true, force: true });
});

// The real installer only allows https; the local test server speaks http.
const options = overrides => ({ get: http.get, allowedProtocols: ["http:"], ...overrides });

function destinationIn(name) {
  const destination = path.join(tempDir, name, "dotver");
  fs.mkdirSync(path.dirname(destination), { recursive: true });
  return destination;
}

test("writes the file when the SHA-256 matches", async () => {
  const destination = destinationIn("ok");

  await downloadVerified(options({ url: `${baseUrl}/asset`, destination, expectedSha256: sha256 }));

  assert.deepEqual(fs.readFileSync(destination), body);
  assert.deepEqual(fs.readdirSync(path.dirname(destination)), ["dotver"]);
});

test("rejects and removes the download when the SHA-256 does not match", async () => {
  const destination = destinationIn("bad");

  await assert.rejects(
    downloadVerified(options({ url: `${baseUrl}/asset`, destination, expectedSha256: "0".repeat(64) })),
    /checksum/i);

  assert.equal(fs.existsSync(destination), false);
  assert.deepEqual(fs.readdirSync(path.dirname(destination)), []);
});

test("follows a redirect to the real asset", async () => {
  const destination = destinationIn("redirect");

  await downloadVerified(options({ url: `${baseUrl}/redirect`, destination, expectedSha256: sha256 }));

  assert.deepEqual(fs.readFileSync(destination), body);
});

test("stops after too many redirects", async () => {
  const destination = destinationIn("loop");

  await assert.rejects(
    downloadVerified(options({ url: `${baseUrl}/loop`, destination, expectedSha256: sha256 })),
    /redirect/i);
  assert.equal(fs.existsSync(destination), false);
});

test("refuses a redirect to a protocol that is not allowed", async () => {
  const destination = destinationIn("elsewhere");

  await assert.rejects(
    downloadVerified(options({ url: `${baseUrl}/elsewhere`, destination, expectedSha256: sha256 })),
    /protocol/i);
  assert.equal(fs.existsSync(destination), false);
});

test("expectedChecksumFor requires a 64-hex SHA-256 for the asset", () => {
  assert.throws(() => expectedChecksumFor({}, "dotver-linux-x64"), /SHA-256/);
  assert.throws(() => expectedChecksumFor({ binaryChecksums: { "dotver-linux-x64": "nope" } }, "dotver-linux-x64"), /SHA-256/);
  assert.equal(
    expectedChecksumFor({ binaryChecksums: { "dotver-linux-x64": "A".repeat(64) } }, "dotver-linux-x64"),
    "a".repeat(64));
});

test("assetNameFor maps supported platforms and rejects the rest", () => {
  assert.equal(assetNameFor("win32", "x64"), "dotver-win-x64.exe");
  assert.equal(assetNameFor("linux", "x64"), "dotver-linux-x64");
  assert.equal(assetNameFor("darwin", "arm64"), "dotver-osx-arm64");
  assert.equal(assetNameFor("darwin", "x64"), "dotver-osx-x64");
  assert.equal(assetNameFor("freebsd", "x64"), null);
});
