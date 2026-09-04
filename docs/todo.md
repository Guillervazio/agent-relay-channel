# Current work

Increment 03 — pay what the backlog says is due. Increments 01 and 02 are closed in
[specs/](specs/).

**Last verified** (4 September 2026, at the close of increment 02):

* `dotnet build` — **0 warnings, 0 errors**, `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on
* `dotnet test` — **49 passed, 0 failed, 0 skipped**, 1 project
* `dotnet format --verify-no-changes` — clean
* `bash scripts/test-all.sh` — **four suites green, 61 checks** (24 REST, 12 CLI, 14 MCP, 11 UI)

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | `PRAGMA synchronous` moves into `OpenAsync`, where every pooled connection meets it | done | this commit |
| 2 | `WaiterRegistry` stops losing a registration to an eviction, with the test that catches it | done | this commit |
| 3 | `ARC_MAX_WAIT` is refused at startup when it would brick the channel | done | this commit |
| 4 | A malformed `--refs` exits `2` instead of sending the message without it | done | this commit |
| 5 | The denylist names the uninstaller it was always meant to name | done | this commit |

Every phase is closed in the same commit that updates its row, and each one leaves
[backlog.md](backlog.md) as it is fixed.

---

## Why these five and not the other seven

The backlog carries twelve findings. These are the five whose written trigger is **now** — four on
merit and one by hand — and nothing else in the file is due. The seven that stay are not smaller;
they are waiting on evidence that has not arrived: a note actually going missing, somebody needing
to know whether `note`-to-self is legal, a `/v2` that could remove a published enum value.

Two of the five are one-line fixes with an outsized failure mode. The `synchronous` pragma is
emitted once, on the initialising connection, and the pragma is per connection: every operation
after startup runs at a durability setting the rules call a decision and nobody chose. The
`WaiterRegistry` race is narrow — a `Register` landing between the emptiness check and the eviction
— but it opens on the ordinary polling pattern, and the class whose tests exist to catch exactly
this kind of fault has no test for it.

## What phase 4 owes the increment after it

`Arc.Cli` has no unit test to assert an exit code with, so phase 4's fix is covered by
`scripts/smoke-cli.sh` and by nothing a gate runs. The seam that would fix that is increment 04,
and the test moves there with it rather than waiting for it here.
