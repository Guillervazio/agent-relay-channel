# Increment 01 — ARC adopts the house doctrine

ARC gained version control, a build that fails on style, and the four-layer context model from
`PlastipackInventoryApp`: `CLAUDE.md` → `.claude/rules/` → `docs/adr/` → `specs/` and `backlog`.
It became the **second consumer** of the `dotnet-house` package, which is the condition that
package's own backlog names as the point at which its v0 exit criteria become testable.

What did **not** change: any behaviour. The four smoke suites pass unmodified, which is the
evidence that 216 type annotations and a package-management move were mechanical.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | Redact the demo token, `.gitignore`, `.gitattributes`, `git init` | done | `9d45be9` |
| 2 | Measure before deciding | done | — |
| 3 | Adopt the `.editorconfig` | done | `e849d4c` |
| 4 | Apply the automatic style fixes | done | `54830a4` |
| 5 | Convert the 171 `var` the fixer could not reach | done | `3c7a6b7` |
| 6 | The CLI's exit codes into a constant table | done | `e5a5b8c` |
| 7 | `Directory.Build.props`, `global.json`, central package management | done | `baefc7e` |
| 8 | The rules, the ADRs, `CLAUDE.md`, `backlog.md`, `todo.md` | done | `8f6b4c2` |
| 9 | See the gate block a turn | done | — |

Verified at close: `restore --force` clean across 4 projects; `build` 0 warnings 0 errors with
`TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on; `build -p:EnforceCodeStyleInBuild=true`
**0 style diagnostics**, from 311; `test` 21 passed 0 skipped; `format --verify-no-changes` clean;
`bash scripts/test-all.sh` **all four suites green**; `arc --help` → 0 and `arc` → 2.

The gate was observed, not assumed: with a deliberate syntax error in `EventStream.cs`,
`stop-gate.ps1` answered **exit 2** and named the build failure; with the file restored, **exit 0**
and the block counter back to zero. Discovery found `Arc.slnx` and `tests/Arc.Tests` with no
`stop-gate.config.json`, which is one of the package's v0 criteria met rather than spent.

---

## What the plan got wrong

| Assumption | What actually happened |
|---|---|
| The `var` conversion is mechanical — "it can be fixed by the fixer, which is why it does not spend a deviation" | The fixer covers **45 of 216**. It does not touch `using var`, `await using var`, `out var`, `foreach (var …)` or deconstructions, which is most of what ARC writes. 171 were converted by hand. The decision survived the measurement, but the argument that justified it did not |
| Style would come back as "hundreds of IDE0011/IDE0040/IDE1006 nobody has measured" | Three diagnostics, total: 216 IDE0008, 90 IDE0011, 5 IDE1006. **No** IDE0040, IDE0044, IDE0161, and no CA rule at any point. The risk was real and the answer was narrow |
| `TreatWarningsAsErrors=true` needs measuring because ARC has unknown warnings | ARC built with **zero** warnings before the flag. The flag broke nothing that existed — it broke three `CS8600`s that **the `var` conversion had just introduced**, because `TryGetValue`'s `out` is null on the false branch and `var` inferred that away. The gate justified itself on its first run, against a defect from the commit before it |
| `EnableNETAnalyzers` is absent, so no analysers run | It defaults to **true** on `net10.0`. CA rules were already running; they were invisible because nothing fired |
| `publish/` is 75 MB | 108 MB |
| The smokes depend on `python3` | They call `python`. `python3` does not exist on this machine; `python` resolves to a conda environment on `PATH` by accident, which is why they pass |
| The plan sequenced `var` before ids "so the id diff stays readable" | Correct, and it also mattered for a reason the plan did not give: `ChannelService.cs:40–41` are both `var` sites and `Guid` sites, and the H010 work never happened this increment |
| `gh` would create the private remote | Neither `gh` nor `winget` is installed. The repository is local-only and the remote is a backlog entry |

Two process notes worth the same weight:

* **`perl -pi` on the `.csproj` files silently deleted three `<ProjectReference>` elements and
  three projects from `Arc.slnx`.** It was caught by `dotnet build`, and recovering cost nothing
  **because phase 1 had already happened** — which is the entire argument for putting `git init`
  before anything that can break. Had this been increment 0 of an unversioned repository, it would
  have been a reconstruction from memory.
* Sweeping regexes over structured files were replaced with exact edits after that.

---

## Decisions

| Decision | Choice | Why |
|---|---|---|
| `var` | Convert all 216, no deviation | Leaving it at `suggestion` would have made the base rule's opening paragraph — "the compiler says it louder and sooner" — false in this repository, which is the failure mode the whole doctrine exists to prevent |
| The five `IDE1006` exit codes | Move to a constant table, do not rename | The rule read them as locals and asked for camelCase. They are the one thing the CLI publishes. `ExitCodes` satisfies the analyser and the contract at once — recorded as [P009](../adr/P009-the-cli-exit-codes-are-contract.md) |
| `shared/architecture.md` | **Do not install it** | Five deviations would be needed. The package's own criterion says two or more means it was a project decision in disguise. A standalone `architecture.project.md` says what ARC's shape is instead of what it is not |
| `shared/entity-framework.md` | Replace with `persistence.project.md` | No ORM. A rule describing something that does not exist is not a contract for the future; it is an invitation to build one |
| The container clause of H008 | Deviate | ARC embeds SQLite. A container would introduce a **different** engine, which is the thing H008 forbids, so obeying the letter would break the intent |
| Spanish test names | Deviate | They already say subject, behaviour and condition. Renaming buys a shape and loses a sentence |
| String identifiers | Deviate | Three surfaces carry them as strings and `PROTOCOL.md` documents them that way. A wrapper would be unwrapped at every boundary |
| `stop-gate.config.json` | Do not write one | Discovery gets ARC right unaided, and "ran in the second consumer with no config" is a criterion the package is being measured against here |
| Four defects found while writing the rules | Record, do not fix | They are a different increment. Fixing them inside the one that installs the rules would make it impossible to tell which change broke what |

---

## Findings that contradicted the docs

Open findings live in [backlog.md](../backlog.md) with their triggers. Recorded here as what this
increment learned:

* **`GET /v1/messages/{id}` and `GET /v1/threads/{id}` authorise nothing.** Found while writing
  the H011 record, and it changed that record: `P006` keeps the 403 for a reason that is a fact
  about `/v1/agents`, and it now has to say out loud that the confidentiality H011 protects does
  not currently exist here.
* **`AddResponseAsync` violates H007.** The status is checked in C# and not repeated in the
  `WHERE`. Two responders both win.
* **The error codes exist in four copies with no test on any of them.** H012 violated twice.
* **`Clamp` truncates an over-long `wait` silently**, making a shortened poll indistinguishable
  from a real timeout.
* **Three `H###` records link to `P###` records that do not travel with them.** In this repository
  those links resolved to nothing — and the numbers `P007`, `P008` and `P011` exist here as
  entirely different decisions, so a coincidence of numbering would have made them resolve to the
  *wrong* record rather than to none. A portable document cannot link to a non-portable one. The
  copies here were flattened to plain text; the fix belongs to the package.

---

## What is not verified yet

* The gate has been seen blocking **when invoked directly**. It has not yet been seen blocking a
  real turn through the harness, which needs a session started after `.claude/settings.json`
  existed.
* `session-doctor.ps1` has not been observed at all. It exits 0 always and writes into the model's
  context, so the way to check it is to ask a fresh session what it was told at start.
* No wait longer than 60 seconds has ever been exercised against a `KeepAliveTimeout` derived from
  `ARC_MAX_WAIT + 60`.
