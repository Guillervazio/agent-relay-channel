# Current work

Increment 05 — the channel explains itself, and the hub starts by hand. Increments 01 to 04 are
closed in [specs/](specs/).

**Last verified** (4 September 2026, at the close of increment 04):

* `dotnet build` — **0 warnings, 0 errors**, `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on
* `dotnet test` — **97 passed, 0 failed, 0 skipped**, 1 project
* `dotnet format --verify-no-changes` — clean
* `bash scripts/test-all.sh` — **four suites green, 64 checks** (24 REST, 15 CLI, 14 MCP, 11 UI)

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | `ServerInstructions`: the MCP handshake carries how to use the channel | done | this commit |
| 2 | `scripts/start-hub.ps1`: run the hub by hand, on loopback or on the LAN | not started |  |
| 3 | `README.md` and `docs/AGENTS.md`: consuming ARC without touching the other repository | not started |  |

Every phase is closed in the same commit that updates its row.

---

## What this increment is for

ARC works, and using it from a real project still means pasting a block of text into that project's
`CLAUDE.md` and `AGENTS.md`. That is the part that does not scale: every repository adopting the
channel gets a copy of the same instructions, and copies drift.

The MCP protocol already has the answer. A server may return `instructions` in its `initialize`
result — natural-language guidance for the model — and `ModelContextProtocol` 2.2.0 exposes it as
`McpServerOptions.ServerInstructions`. The channel can explain **itself**, once, from the hub, to
every client that connects.

That leaves the consuming repository with nothing to change at all: `claude mcp add --scope user`
writes to `~/.claude.json` and applies to every project, and `~/.codex/config.toml` is already
global to the user.

**Phase 1 touches the wire**, so `docs/PROTOCOL.md` changes in the same commit and `smoke-mcp.sh`
gains a check that `instructions` arrives and is not empty. Whether a given client injects it into
its model's context is a client decision, not a protocol one, and the README has to say so rather
than promise it.

## Why the hub needs a way to start by hand

`README.md` documents exactly one way to run it: `install-hub.ps1`, which needs an administrator
console and installs a Windows service. There is no documented way to just start it, which is what
anybody does the first time.

`scripts/start-hub.ps1` covers both topologies with one switch, generates or reuses the token, and
says what is missing for LAN rather than failing later — the firewall rule stays
`install-hub.ps1 -FirewallOnly`, because creating it needs elevation and starting the hub does not.

## What phase 3 must not do

`docs/AGENTS.md` stops being "the text to paste into every repository" and becomes the fallback for
a client that does not read `instructions`. It does not gain a second home: if the wiring for a
consuming repository needs writing down, it goes there, not into a new `templates/` directory whose
only effect would be two copies of the same prose.
