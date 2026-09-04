# Current work

Increment 02 — make the code obey what the rules say. Increment 01 is closed in
[specs/01-arc-adopts-the-house-doctrine.md](specs/01-arc-adopts-the-house-doctrine.md).

**Last verified** (4 September 2026):

* `dotnet restore --force` clean across 4 projects
* `dotnet build` — **0 warnings, 0 errors**, `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on
* `dotnet test` — **44 passed, 0 failed, 0 skipped**, 1 project (was 21 at the start of this increment)
* `dotnet format --verify-no-changes` — clean
* `bash scripts/test-all.sh` — **four suites green, 61 checks**
* `stop-gate.ps1` invoked directly: **exit 2** on a broken build, **exit 0** restored

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | H007: the status moves into the `WHERE`; two concurrent responders, one wins | done | `aeef227` |
| 2 | H012: one `ArcErrors`; tests that freeze the literals and one that discovers divergence | done | `45c4f48` |
| 3 | H013: seven codes to 422, `invalid_json` keeps 400, `PROTOCOL.md` and the smokes in the same commit | done | `661d574` |
| 4 | `ValidateWait` refuses instead of clamping, and refuses **before** creating anything | done | `661d574` |
| 5 | `TimeProvider` injected into `ChannelService` and the hub's two timestamps | **not started** |  |
| 6 | Tests for `ChannelService` | done | `ae9757a` |
| 7 | The rules and the published contract say what the code does | done | this commit |

Every phase is closed in the same commit that updates its row.

---

## What phase 5 has to do

`DateTimeOffset.UtcNow` is read directly in `ChannelService`, `MessageStore` and
`Arc.Hub/Program.cs`. Until it is injected, no test can control time, and
`WaiterRegistryTests` and `ChannelServiceTests` both measure real elapsed milliseconds — which is
the part of the suite most exposed to a slow machine.

It needs one package approval, for `Microsoft.Extensions.TimeProvider.Testing`, and the appendix
table updated in the same change.

---

## What phase 7 found and did not fix

Phase 7 was the mirror of this increment's title: instead of making the code obey the rules, it
made the rules and the two public documents describe the code. The prose was wrong in ten places,
including a `README.md` sentence promising that `--wait 600` "is not an error, it waits 300" —
false since phase 4, and carried into English intact by the translation.

It also found nine defects in the code and **fixed none of them**, by scope. They are in
[backlog.md](backlog.md) with a trigger each. Four are marked due now on merit: the `synchronous`
pragma that is not in force, the `WaiterRegistry` race that can orphan a waiter, the CLI
discarding a malformed `--refs` in silence, and `ARC_MAX_WAIT` accepting a value that bricks the
hub.

---

Two things are owed that are not phases, because they are not code:

* **The GitHub remote.** `gh` is not installed; the repository is local-only.
* **The `.claude/settings.json` denylist**, which names a pattern that matches nothing.

Both are in [backlog.md](backlog.md) with what makes them due.
