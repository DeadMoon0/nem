![Icon](https://raw.githubusercontent.com/DeadMoon0/NEM/main/nem/Assets/Icon.svg)
[![NuGet Version](https://img.shields.io/nuget/v/nem?label=nuget%20nem)](https://www.nuget.org/packages/nem)

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

- Windows, Linux or macOS — installing and downloading Node works on all three (CI verifies Windows and Linux)
- [.NET](https://dotnet.microsoft.com) runtime (installed as a [dotnet tool](#installation))

## Installation

```bash
# from a published feed
dotnet tool install -g nem

# once: add the nem proxy directory to your PATH
nem setup
```

`nem setup` puts the nem proxy directory (`%APPDATA%\nem\proxy` on Windows, `~/.config/nem/proxy` on Unix) on your PATH. Every tool that belongs to a nem env gets a small proxy script in that directory, so tools can be invoked from any folder. On Windows this patches the machine PATH (one elevated run is enough for everyone on the box); on Unix it appends an `export PATH=...` line to your shell rc files (`~/.profile`, plus `~/.bashrc` / `~/.zshrc` / `~/.zprofile` when they exist), so just run it once per user.

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
#   1. Downloads the declared Node version (cached in the nem system
#      directory, see Caches) and copies it into .nenv/
#   2. Installs every tool declared in nem.json that is missing
#   3. Creates global proxies for npm, npx and every binary the
#      installed packages expose (from their package.json "bin" field)

$ nem install --clean [path]
# Same, but wipes the cached zip, the cached extraction and .nenv first
```

`nem install` is idempotent — re-run it any time; only what is missing gets installed.

- If the Node version in `.nenv` does not match the declared version (e.g. after
  editing `nem.json`), it is removed and the declared version is installed.
- Proxies for tools that are no longer installed in the env are pruned, so the
  proxy directory never holds stale shims.

### Security audit

After installing, nem audits **every package** in the env (including
transitive dependencies of your tools) against the npm registry's advisory
database and prints the result:

- a green confirmation when nothing is known to be vulnerable,
- a table of affected packages, installed versions, severity and advisory
  links when there are findings (sorted critical first).

The audit is best effort — if the registry cannot be reached, nem notes it
and continues; it never fails the install.

### Use tools
```bash
$ ng serve
# The global proxy finds the env by walking up from your CWD,
# then runs the .nenv copy of ng with the env's node.

$ cd ../other-project  # no nem.json here
$ ng serve
# No env found -> falls back to the system ng.
```

You can also invoke the env machinery explicitly:

```bash
$ nem run ng serve
# Runs ng from the env of the current directory; everything after the
# tool name is passed to the tool. A literal '--' is also understood
# and optional ('nem run ng -- serve' works too). Without an env, nem
# falls back to the system tool of the same name.
```

### Update the env
```bash
$ nem update [what] [path] [-t|--tools]

$ nem update
# In an interactive terminal: shows a table of the Node version and all
# tools (declared / installed / newest supported) and asks for each item
# that has an update. Outside a terminal (CI, scripts): prints the same
# table plus the exact commands to apply the updates, and changes nothing.

$ nem update 20
# Resolves '20' to the newest matching release, updates nem.json and
# installs it into .nenv. Tools keep their versions, unless you add
# --tools (or answer the interactive question) so they are updated to the
# newest versions the new Node supports.

$ nem update 18.12.0
# Switches the env to exactly Node.js 18.12.0.

$ nem update typescript
# Updates just one tool to the newest version the env's Node supports
# (also: 'typescript@5.6.3' for an exact version, '@angular/cli' for a
# scoped name).

$ nem update all
# Node to the newest stable release, and every tool to the newest version
# that release supports.
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
.nenv/                                  # on Windows (flat copy of the dist)
  node.exe, npm(.cmd/.ps1), npx(...)   # the Node distribution
  node_modules/<package>               # npm-installed env tools
  <tool>(.cmd/.ps1)                    # npm shims for the env tools

.nenv/                                  # on Unix (npm --prefix layout)
  bin/node, bin/npm, bin/<tool>         # runtime + shims
  lib/node_modules/<package>            # npm-installed env tools
```

**3. Proxies** — Transparent routing
```
proxy dir on PATH (e.g. %APPDATA%\nem\proxy\ng.bat on Windows,
~/.config/nem/proxy/ng on Unix)
  -> nem run ng -- <args>
       1. walk up from CWD looking for nem.json
       2. if found: run the env's ng (shim + env node)
       3. if not found: run the system ng (fallback)
```

Proxies exist as `ng`, `ng.bat` (cmd) and `ng.ps1` (PowerShell). `nem install` re-creates the proxies for `npm`, `npx` and every tool in `nem.json`, so a fresh checkout plus `nem install` makes all declared tools work.

**4. Tool installation** — npm-powered
```
nem install
  -> for each missing tool in nem.json:
       npm install -g --no-audit <tool>@<version> --prefix .nenv
  -> reads each package's "bin" entries, creates a proxy per binary
  -> prunes proxies for tools that are no longer installed
  -> audits the whole tree against the npm advisory database
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
The system directory is `%APPDATA%\nem\` on Windows and `~/.config/nem/` on Unix.

| Path | Content |
| --- | --- |
| `...\download\` | Node distribution downloads (kept for reuse) |
| `...\extract\` | Extracted Node distributions |
| `...\proxy\` | Global tool proxies (on your PATH) |

---

## Development

The solution contains three projects:

| Project | Purpose |
| --- | --- |
| `nem/` | The CLI (commands, services, proxy templates) and the packaged dotnet tool |
| `nem.Common/` | Shared models (`nem.json`) and path management |
| `nem.Tests/` | xunit unit tests (version logic, env layout, config, proxies, update planning) |

```bash
dotnet build nem.slnx -c Release   # build everything
dotnet test nem.slnx -c Release    # run the unit tests
```

The tests never touch the network: Node and npm version lookups are replaced
with fakes, and all filesystem state lives in unique temp directories that are
deleted afterwards.

---

## Why nem?

- **Lightweight** — just wraps npm and a downloaded Node distribution
- **Transparent** — tools work exactly as they normally do once the proxy dir is on your PATH
- **Reproducible** — `nem.json` locks Node and tool versions across your team
- **Portable** — one `.nenv/` per project, no system pollution, safe to delete
