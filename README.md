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
$ nem init 18.12.0 [path]
# Creates nem.json (NodeVersion + Tools) and an empty .nenv/ folder
# in the current or the given folder
```

### Install the environment
```bash
$ nem install [path]
# Downloads the declared Node version (cached in %APPDATA%\nem\download)
# and copies it into .nenv/, then (re)creates proxies for npm, npx
# and every tool listed in nem.json

$ nem install --clean [path]
# Same, but wipes the cached zip, the cached extraction and .nenv first
```

### Manage tools
```bash
$ nem tool add ng@15.2
# Resolves 'ng' against npm, installs it into the .nenv via
# 'npm install -g ng@15.2 --prefix .nenv', creates a proxy for 'ng'
# and records the resolved version in nem.json

$ nem tool add ts-node@10.9.0
$ nem tool list
$ nem tool remove ts-node
```

A package can expose several binaries (e.g. `ts-node` ships `ts-node`, `ts-node-esm`, ...); nem proxies every new binary it finds.

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
nem tool add ng@15.2
  -> npm install -g ng@15.2 --prefix .nenv
```
npm handles dependency resolution; the package and its shims land in `.nenv`. nem only records the result in `nem.json` and creates the global proxies.

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
