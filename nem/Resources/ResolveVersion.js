// nem package-version resolver.
//
// Runs under `node -e <this-file> <package> <nodeVersion> <range?>`.
// Prints the resolved exact version (or nothing) and exits 0.
//
// Why this is node instead of C#: engines.node in package metadata is written
// in npm's semver RANGE syntax (^, ~, ||, x-ranges, ...). The only faithful
// evaluator of that syntax is npm's own semver module, which every Node
// distribution ships inside npm. Loading it here (via the env's node) means
// nem resolves versions exactly the way npm itself would, with no extra
// dependency.

const path = require('path');
const { createRequire } = require('module');

const pkg = process.argv[1];
const nodeV = process.argv[2] || '';
const range = process.argv[3] || '';

// Locate npm's bundled semver (same implementation npm uses for ranges).
let sv = null;
const nodeDir = path.dirname(process.execPath);
for (const d of [path.join(nodeDir, 'node_modules', 'npm'), path.join(nodeDir, 'lib', 'node_modules', 'npm')]) {
    try { sv = createRequire(path.join(d, 'package.json'))('semver'); break; } catch (e) { }
}
if (!sv) { try { sv = require('semver'); } catch (e) { } }
if (!sv) { console.log(''); process.exit(0); }

const registry = (process.env.npm_config_registry || 'https://registry.npmjs.org/').replace(/\/?$/, '/');
const url = registry + pkg.replace('/', '%2F');

async function fetchDoc() {
    if (globalThis.fetch) {
        const res = await fetch(url);
        if (!res.ok) throw new Error('registry ' + res.status);
        return res.json();
    }
    return new Promise((resolve, reject) => {
        const mod = require(url.startsWith('https:') ? 'https' : 'http');
        mod.get(url, r => {
            let s = '';
            r.on('data', c => { s += c; });
            r.on('end', () => { try { resolve(JSON.parse(s)); } catch (e) { reject(e); } });
        }).on('error', reject);
    });
}

(async () => {
    let doc;
    try { doc = await fetchDoc(); } catch (e) { console.log(''); process.exit(0); }

    const versions = doc.versions || {};
    const tags = doc['dist-tags'] || {};
    const all = Object.keys(versions).filter(v => sv.valid(v));
    const stable = all.filter(v => !sv.prerelease(v));

    // Does the env's node version satisfy this release's engines.node range?
    const nodeOk = v => {
        const r = (versions[v].engines || {})['node'];
        if (!r) return true;
        try { return sv.satisfies(nodeV, r, { includePrerelease: true }); } catch (e) { return false; }
    };

    let pool;
    if (range && all.includes(range)) pool = [range];                      // exact version
    else if (range && tags[range]) pool = [tags[range]];                   // dist-tag (latest, next, ...)
    else if (range) pool = stable.filter(v => { try { return sv.satisfies(v, range); } catch (e) { return false; } });
    else pool = stable.filter(nodeOk);                                     // newest compatible with the env node

    if (pool.length === 0 && !range) pool = all.filter(nodeOk);            // allow prereleases as a last resort
    if (pool.length === 0) pool = stable.length ? stable : all;            // no node filter at all
    pool.sort((a, b) => sv.rcompare(a, b));
    console.log(pool[0] || '');
})();
