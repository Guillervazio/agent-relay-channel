# Current work

Increment 01 — ARC adopts the house doctrine.

**Last verified** (4 September 2026, commit `baefc7e`):

* `dotnet restore --force` clean across 4 projects
* `dotnet build` — **0 warnings, 0 errors**, with `TreatWarningsAsErrors` and
  `EnforceCodeStyleInBuild` on
* `dotnet build -p:EnforceCodeStyleInBuild=true` — **0 style diagnostics** (was 311: 216 IDE0008,
  90 IDE0011, 5 IDE1006)
* `dotnet test` — **21 passed, 0 failed, 0 skipped**, 1 project
* `dotnet format --verify-no-changes` — clean
* `arc --help` → 0, `arc` → 2, `arc noexiste` → 2

Not verified: `bash scripts/test-all.sh` has not been run since the style commits. The gate has
not been seen blocking a turn.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | Redact the demo token, `.gitignore`, `.gitattributes`, `git init` | done | `9d45be9` |
| 2 | Measure before deciding: warnings, style cost, what the fixer covers | done | — |
| 3 | Adopt the `.editorconfig` | done | `e849d4c` |
| 4 | Apply the automatic style fixes | done | `54830a4` |
| 5 | Convert the 171 `var` the fixer could not reach | done | `3c7a6b7` |
| 6 | The CLI's exit codes into a constant table | done | `e5a5b8c` |
| 7 | `Directory.Build.props`, `global.json`, central package management | done | `baefc7e` |
| 8 | The rules, the ADRs, `CLAUDE.md`, this file | in progress | — |
| 9 | Install the plugin and **see the gate block a turn** | not started | — |
| 10 | Fix what the rules now say: H007, the four error-code copies, H013, `TimeProvider` | not started | — |
| 11 | Tests for `ChannelService`, which today has none | not started | — |

Every phase is closed in the same commit that updates its row.

Deferred work with its trigger: [backlog.md](backlog.md).
