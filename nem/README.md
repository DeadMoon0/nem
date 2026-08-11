# nem

**Node Environment Manager** — Local Node.js and tool environments for your project.

## The Idea

`nem` lets you declare and manage Node.js versions and CLI tools **per project**, similar to Python's `.venv`. Instead of relying on globally installed tools or requiring developers to juggle multiple Node versions, `nem` creates a project-local environment that:

- **Declares** required versions in `nem.json` (committed to git)
- **Installs** Node and tools into a `.nem/` folder (gitignored)
- **Routes** tool invocations transparently — use the local version if in a managed project, fall back to global otherwise

No installation pollution. No "works on my machine" surprises. Everyone on the team uses the same toolchain.

---

## Usage

### Initialize a project
```bash
$ nem init
# Creates nem.json and .nem/ folder
```

### Install a Node version
```bash
$ nem install 18.12.0
# Downloads Node 18.12.0 into .nem/bin/
# Updates nem.json
```

### Add tools
```bash
$ nem tool add ng@15.0.0
# Installs Angular CLI into .nem/ using local npm
# Creates a shim so `ng` works transparently
# Updates nem.json

$ nem tool add ts-node@10.9.0
```

### Use tools normally
```bash
$ ng serve
# Shim detects you're in a nem project
# Runs local .nem/bin/ng automatically

$ cd ../other-project  # no nem.json here
$ ng serve
# Falls back to global ng
```

### Share with your team
```bash
# Commit nem.json
$ git add nem.json
$ git commit -m "Lock node 18.12.0, ng 15.0.0, ts-node 10.9.0"

# Team member clones, runs:
$ nem install
# Restores the exact same environment
```

---

## How It Works (Rough)

**1. nem.json** — Your environment declaration
```json
{
  "node": "18.12.0",
  "tools": {
    "ng": "15.0.0",
    "ts-node": "10.9.0"
  }
}
```

**2. .nem/ folder** — Isolated environment
```
.nem/
  bin/        # Node + tool binaries
  lib/
    node_modules/  # Tool dependencies
```

**3. Shims** — Transparent routing
```bash
# Global shim at ~/.nem/shims/ng
# When you run: ng serve
# Shim does:
#   1. Walk up from CWD looking for nem.json
#   2. If found: use .nem/bin/ng
#   3. If not found: use global ng
```

**4. Tool installation** — npm-powered
```bash
nem tool add ng@15
# Runs: npm install -g ng@15 --prefix .nem
# npm handles dependency resolution
# Binary lands in .nem/bin/ng
```

---

## Why nem?

- **Lightweight** — Just wraps npm, doesn't reinvent package management
- **Transparent** — Tools work exactly as they normally do
- **Reproducible** — nem.json locks versions across your team
- **Portable** — Works across projects, doesn't pollute your system
