# Increment 02 — the code obeys what the rules say

Increment 01 installed the doctrine and, in the act of writing it down, found that the code did
not match it. This increment closed that gap in both directions: four phases changed the code to
obey a rule, and one changed the rules to describe the code. It is the increment where the
repository stopped asserting things about itself that were not true.

No endpoint was added and no field moved. `PROTOCOL.md` changed twice — once because a refusal's
status code changed, once because the CLI's exit codes and the `hello` event were undeclared — and
both times in the commit that made the statement true.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | H007: the status moves into the `WHERE`; two concurrent responders, one wins | done | `aeef227` |
| 2 | H012: one `ArcErrors`; tests that freeze the literals and one that discovers divergence | done | `45c4f48` |
| 3 | H013: seven codes to 422, `invalid_json` keeps 400 | done | `661d574` |
| 4 | `ValidateWait` refuses instead of clamping, and refuses **before** creating anything | done | `661d574` |
| 5 | `TimeProvider` injected into `ChannelService`, `MessageStore`, `EventStream` and the hub | done | `a669aa2` `1e483bf` |
| 6 | Tests for `ChannelService` | done | `ae9757a` |
| 7 | The rules and the published contract say what the code does | done | `781ed90` `97df530` |

Verified at close (4 September 2026): `build` **0 warnings, 0 errors**; `test` **49 passed, 0
failed, 0 skipped**, from 21 at the start of the increment; `format --verify-no-changes` clean;
`bash scripts/test-all.sh` **four suites green, 61 checks** — 24 REST, 12 CLI, 14 MCP, 11 UI.

---

## What phase 5 decided

`TimeProvider` enters as an **optional constructor parameter defaulting to `TimeProvider.System`**,
not as a required one and not as an interface. Three consequences were the reason:

* It is abstract and lives in the BCL, so it is not the first interface
  [H002](../adr/house/H002-single-implementation-interfaces.md) would demand a test about, and
  production takes no package for it. That was settled **by compiling** against `net10.0` with no
  reference at all — the workflow's own instruction for answering "does the framework already cover
  this", and the step this repository has now skipped zero times instead of twice.
* Defaulting it meant no existing call site changed, so the 44 tests that already passed are the
  evidence the refactor moved no behaviour. A required parameter would have edited every one of
  them and destroyed that evidence in the same commit.
* `ChannelEvent.At` became `required` rather than keeping its `= DateTimeOffset.UtcNow` default. A
  defaulted property that reads the real clock is a second clock inside the type, unreachable from
  the one that was just injected — the injection would have been true of the code and false of the
  behaviour.

The package that provides `FakeTimeProvider` arrived in the commit that *reads* it, not before.
The workflow forbids a commit that produces code with no reader, and a `PackageReference` nothing
uses is exactly that, with the aggravating property that nothing fails: the build stays green and
only somebody noticing stands between it and the repository.

## What phase 5 did not do, and why that is not an oversight

Two readers of the real clock survive, both named in [backlog.md](../backlog.md) with what makes
them due:

* **`WaiterRegistry`.** Its waits are `Task.Delay`, so a fake clock does not reach them unless the
  registry itself takes a `TimeProvider` — a change to the wait mechanism, in the one class where a
  race already has an open finding. Timestamp work and concurrency work in one commit makes a
  failure ambiguous about which half caused it.
* **`Arc.Cli`.** It measures elapsed time for its own progress line. It has no composition root and
  no unit test to inject anything from, and manufacturing one here would be the seam work of a
  later increment done under the wrong heading.

---

## What this increment learned

* **A rule that is false is worse than no rule, because it gets followed.** Phase 7 found the prose
  wrong in ten places, and the worst of them were rules describing defects that phases 1 and 2 had
  already fixed — true when written, never reconciled when the code changed. That is what the
  `reconcile-rules` step exists to prevent, and it was not run at the time.
* **A translation carries a false sentence intact.** The README's promise that `--wait 600` "is not
  an error, it waits 300" had been false since phase 4 and crossed into English unchanged, because
  translating checks that a sentence says the same thing, never that it is true.
* **Nine defects were found and none fixed**, by scope. They are in the backlog with a trigger
  each; four are due now on merit and are the subject of the next increment.
