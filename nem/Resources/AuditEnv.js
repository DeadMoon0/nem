// AuditEnv.js - security audit for a nem env's tool tree.
//
// Invoked as:  node -e "<this script>" <modulesRoot> <registry>
//
// Walks the env's global modules root, collects every package name + the
// version(s) actually on disk (the npm/corepack bundles shipped with node
// itself are excluded - they are not part of the user's tools), POSTs the
// name -> [versions] map to the registry's bulk advisory endpoint, and
// prints a JSON report on stdout:
//
//   { "packages": <n>, "advisories": [ { name, versions, severity, title, url, vulnerableVersions } ] }
//
// Exits 0 on success, 3 when the registry is unreachable / answers badly.

'use strict';

const fs = require('fs');
const path = require('path');
const https = require('https');
const http = require('http');

const modulesRoot = process.argv[1] || '';
const registry = (process.argv[2] || 'https://registry.npmjs.org').replace(/\/+$/, '');

// Packages that ship with node itself and are not nem-installed tools.
const EXCLUDED = new Set(['npm', 'corepack']);

const found = new Map(); // name -> Set(versions)

function readPackage(dir) {
  try {
    const pkg = JSON.parse(fs.readFileSync(path.join(dir, 'package.json'), 'utf8'));
    if (pkg && typeof pkg.name === 'string' && typeof pkg.version === 'string')
      return pkg;
  } catch (e) {
    // Not a package dir.
  }
  return null;
}

function record(pkg) {
  let set = found.get(pkg.name);
  if (!set) {
    set = new Set();
    found.set(pkg.name, set);
  }
  set.add(pkg.version);
}

function walk(modDir, depth) {
  if (depth > 8 || !modDir)
    return;
  let entries;
  try {
    entries = fs.readdirSync(modDir, { withFileTypes: true });
  } catch (e) {
    return;
  }
  for (const ent of entries) {
    if (!ent.isDirectory())
      continue;
    const base = path.join(modDir, ent.name);
    if (ent.name.startsWith('@')) {
      // Scoped namespace: the packages live one level deeper.
      let sub;
      try {
        sub = fs.readdirSync(base, { withFileTypes: true });
      } catch (e) {
        continue;
      }
      for (const s of sub) {
        if (!s.isDirectory() || s.name.startsWith('.'))
          continue;
        const pkg = readPackage(path.join(base, s.name));
        if (!pkg)
          continue;
        record(pkg);
        walk(path.join(base, s.name, 'node_modules'), depth + 1);
      }
    } else if (!ent.name.startsWith('.') && !EXCLUDED.has(ent.name)) {
      const pkg = readPackage(base);
      if (!pkg)
        continue;
      record(pkg);
      walk(path.join(base, 'node_modules'), depth + 1);
    }
  }
}

walk(modulesRoot, 0);
for (const excluded of EXCLUDED)
  found.delete(excluded);

const names = [...found.keys()].sort();
const payload = {};
for (const name of names)
  payload[name] = [...found.get(name)].sort();

function fail(message) {
  console.error(message);
  process.exit(3);
}

function post(body) {
  return new Promise((resolve, reject) => {
    let u;
    try {
      u = new URL(registry + '/-/npm/v1/security/advisories/bulk');
    } catch (e) {
      reject(e);
      return;
    }
    const mod = u.protocol === 'http:' ? http : https;
    const req = mod.request(
      u,
      {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Content-Length': Buffer.byteLength(body),
        },
      },
      (res) => {
        let data = '';
        res.setEncoding('utf8');
        res.on('data', (chunk) => (data += chunk));
        res.on('end', () => resolve({ status: res.statusCode || 0, body: data }));
      }
    );
    req.on('error', reject);
    req.write(body);
    req.end();
  });
}

// Optional, best-effort: refine which of the installed versions actually fall
// into the advisory range using npm's bundled semver.
let semver = null;
try {
  semver = require(path.join(modulesRoot, 'npm', 'node_modules', 'semver'));
} catch (e) {
  semver = null;
}

function affectedVersions(name, range) {
  const versions = [...(found.get(name) || [])];
  if (!semver || !range)
    return versions; // Conservative: report all installed versions.
  const out = [];
  for (const v of versions) {
    try {
      if (semver.satisfies(v, range))
        out.push(v);
    } catch (e) {
      out.push(v);
    }
  }
  return out.length ? out : versions;
}

function main() {
  if (names.length === 0) {
    console.log(JSON.stringify({ packages: 0, advisories: [] }));
    return;
  }

  post(JSON.stringify(payload))
    .then((res) => {
      if (res.status < 200 || res.status >= 300)
        fail('advisory endpoint returned HTTP ' + res.status);
      let data;
      try {
        data = JSON.parse(res.body);
      } catch (e) {
        fail('invalid response from advisory endpoint');
      }
      const advisories = [];
      for (const [name, list] of Object.entries(data)) {
        for (const a of list || []) {
          advisories.push({
            name: name,
            versions: affectedVersions(name, a.vulnerable_versions),
            severity: a.severity || 'unknown',
            title: a.title || 'Unknown advisory',
            url: a.url || '',
            vulnerableVersions: a.vulnerable_versions || '',
          });
        }
      }
      console.log(JSON.stringify({ packages: names.length, advisories: advisories }));
    })
    .catch((err) => fail(String((err && err.message) || err)));
}

main();
