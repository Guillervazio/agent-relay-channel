# Current work

Increment 01 is closed — [specs/01-arc-adopts-the-house-doctrine.md](specs/01-arc-adopts-the-house-doctrine.md).
Nothing is in progress.

**Last verified** (4 September 2026, commit `8f6b4c2` plus this one):

* `dotnet restore --force` clean across 4 projects
* `dotnet build` — **0 warnings, 0 errors**, with `TreatWarningsAsErrors` and
  `EnforceCodeStyleInBuild` on
* `dotnet build -p:EnforceCodeStyleInBuild=true` — **0 style diagnostics** (was 311)
* `dotnet test` — **21 passed, 0 failed, 0 skipped**, 1 project
* `dotnet format --verify-no-changes` — clean
* `bash scripts/test-all.sh` — **four suites green**: REST, CLI, MCP, observer UI
* `arc --help` → 0, `arc` → 2, `arc noexiste` → 2
* `stop-gate.ps1` invoked directly: **exit 2** on a deliberately broken build, **exit 0** restored

---

## Increment 02 — make the code obey what the rules now say

Not started. In this order, because each step is a commit and the last two depend on the first
three landing in `ChannelService`.

| # | Phase | Status |
|---|---|---|
| 1 | H007: `UPDATE … AND status <> 'answered'`, check rows affected, roll back and throw `already_answered` on zero. Test: two concurrent `RespondAsync`, exactly one wins | not started |
| 2 | H012: one `ArcErrors` definition in `Arc.Core`; the other three copies reference it; `PROTOCOL.md` stays the published table; **tests spell the literals** | not started |
| 3 | H013: the four codes move 400 → 422, `bad_agent` decided, `PROTOCOL.md` in the same commit | not started |
| 4 | `Clamp` refuses an out-of-range `wait` with 422 instead of truncating | not started |
| 5 | `TimeProvider` injected into `ChannelService` and the hub's two timestamps; one package approval for `FakeTimeProvider` | not started |
| 6 | Tests for `ChannelService`, which today has none — and where H002 becomes binding, because the temptation is to add the repository's first interface to fake the store | not started |

Every phase is closed in the same commit that updates its row.

Two things are owed before any of that and are not phases, because they are not code:

* The GitHub remote. `gh` is not installed; the repository is local-only.
* The `.claude/settings.json` denylist, which currently names a pattern that matches nothing.

Both are in [backlog.md](backlog.md) with what makes them due.
