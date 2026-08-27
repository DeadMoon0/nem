# nem

**Node Environment Manager** — Local Node.js and tool environments for your project.

## The Idea

`nem` lets you declare and manage Node.js versions and CLI tools **per project**, similar to Python's `.venv`. Instead of relying on globally installed tools or requiring developers to juggle multiple Node versions, `nem` creates a project-local environment that:

- **Declares** the required Node version and tools in `nem.json` (committed to git)
- **Installs** Node and tools into a `.nenv/` folder (gitignored)
- **Routes** tool invocations transparently — use the local version when in a managed project, fall back to the system tool otherwise

No installation pollution. No "works on my machine" surprises. Everyone on the team uses the same toolchain.

---

## Requirements

- Windows (installing and downloading Node is currently Windows-only; the rest is cross-platform by design)
- [.NET](https://dotnet.microsoft.com) runtime (installed as a [dotnet tool](#installation))

## Installation

```bash
# from a published feed
dotnet tool install -g nem

# once, on Windows: adds the nem proxy directory to your machine PATH
nem setup
```

`nem setup` puts `%APPDATA%\nem\proxy` on your PATH. Every tool that belongs to a nem env gets a small proxy script in that directory, so tools can be invoked from any folder. (On Unix you just add `%APPDATA%\nem\proxy` to your PATH yourself.)

## Usage

### Initialize a project
```bash
$ nem init <nodeVersion> [path]
# Validates the version against nodejs.org and creates nem.json
# (NodeVersion + Tools) in the current or the given folder
```

The version is validated before anything is written:

- An exact version (`18.12.0`, also accepted with a leading `v`) must exist on
  nodejs.org, otherwise init fails.
- A partial version (`22` or `18.12`) resolves to the newest matching release
  and the **resolved** version is what gets written to `nem.json`, e.g. `22`
  → `22.23.2`.

### Manage tools (declarative)
```bash
$ nem tool add ts-node@10.9.0
# Records ts-node@10.9.0 in nem.json. Nothing is installed yet.

$ nem tool add @angular/cli
# No version given? nem resolves the newest stable version whose
# engines.node field supports the env's NodeVersion (from the npm
# registry) and records that exact version in nem.json.

$ nem tool list
# Declared tools with installed / not installed status

$ nem tool remove ts-node
# Removes it from nem.json; if the env has it installed, uninstalls
# it and deletes its proxies.
```

The `nem tool` commands only touch `nem.json` (and clean the env on remove) — they never install packages. That keeps them fast and lets you edit declarations before materializing.

### Install the environment
```bash
$ nem install [path]
# The single materialization step:
#   1. Downloads the declared Node version (cached in %APPDATA%\nem\download)
#      and copies it into .nenv/
#   2. Installs every tool declared in nem.json that is missing
#   3. Creates global proxies for npm, npx and every binary the
#      installed packages expose (from their package.json "bin" field)

$ nem install --clean [path]
# Same, but wipes the cached zip, the cached extraction and .nenv first
```

`nem install` is idempotent — re-run it any time; only what is missing gets installed.

### Use tools
```bash
$ ng serve
# The global proxy finds the env by walking up from your CWD,
# then runs the .nenv copy of ng with the env's node.

$ cd ..\other-project  # no nem.json here
$ ng serve
# No env found -> falls back to the system ng.
```

You can also invoke the env machinery explicitly:

```bash
$ nem run ng -- serve
# Runs ng from the env of the current directory; everything after
# '--' is passed to the tool. Without an env, nem falls back to
# the system tool of the same name.
```

### Share with your team
```bash
# Commit nem.json
$ git add nem.json
$ git commit -m "Lock node 18.12.0, ng 15.2.11"

# Team member clones and runs:
$ nem install
# Restores the exact same environment
```

---

## How It Works

**1. nem.json** — Your environment declaration
```json
{
  "NodeVersion": "18.12.0",
  "Tools": [
    { "ToolName": "@angular/cli", "ToolVersion": "15.2.11" },
    { "ToolName": "ts-node", "ToolVersion": "10.9.0" }
  ]
}
```

**2. `.nenv/` folder** — The isolated environment
```
.nenv/
  node.exe, npm(.cmd/.ps1), npx(...)   # the Node distribution (flat copy)
  node_modules/<package>               # npm-installed env tools
  <tool>(.cmd/.ps1)                    # npm shims for the env tools
```

**3. Proxies** — Transparent routing
```
%APPDATA%\nem\proxy\ng.bat   (on your PATH)
  -> nem run ng -- <args>
       1. walk up from CWD looking for nem.json
       2. if found: run .nenv\ng (shim + env node)
       3. if not found: run the system ng (fallback)
```

Proxies exist as `ng`, `ng.bat` (cmd) and `ng.ps1` (PowerShell). `nem install` re-creates the proxies for `npm`, `npx` and every tool in `nem.json`, so a fresh checkout plus `nem install` makes all declared tools work.

**4. Tool installation** — npm-powered
```
nem install
  -> for each missing tool in nem.json:
       npm install -g <tool>@<version> --prefix .nenv
  -> reads each package's "bin" entries, creates a proxy per binary
```
npm handles dependency resolution; the packages and their shims land in `.nenv`. Proxies are derived from the installed `package.json` files, so every binary a tool ships (e.g. `ts-node` also ships `ts-node-esm`, `ts-script`, ...) gets one.

**5. Version resolution** — registry + engines.node
```
nem tool add @angular/cli   (env NodeVersion = 18.12.0)
  -> fetches the packument from the npm registry
  -> picks the newest stable version whose engines.node range
     allows 18.12.0  =>  16.2.16, not the latest 22.x
  -> records @angular/cli@16.2.16 in nem.json
```
An explicit `@version` or dist-tag is used as-is (validated against the registry).

### Caches
| Path | Content |
| --- | --- |
| `%APPDATA%\nem\download\` | Node zip downloads (kept for reuse) |
| `%APPDATA%\nem\extract\` | Extracted Node distributions |
| `%APPDATA%\nem\proxy\` | Global tool proxies (on your PATH) |

---

## Why nem?

- **Lightweight** — just wraps npm and a downloaded Node distribution
- **Transparent** — tools work exactly as they normally do once the proxy dir is on your PATH
- **Reproducible** — `nem.json` locks Node and tool versions across your team
- **Portable** — one `.nenv/` per project, no system pollution, safe to delete
