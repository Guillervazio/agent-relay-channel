# Increment 06 — a machine that is not this one

ARC stopped being a thing that runs here. Nothing in the channel changed: one hub per network with
one shared token is exactly the deployment a tool anybody installs has, so
[P003](../adr/P003-sqlite-on-a-file.md), [P004](../adr/P004-one-token-and-an-agent-header.md) and
[P010](../adr/P010-the-observer-page-is-unauthenticated.md) all held. What changed is everything
around it — the installer that had never been run in the console it targets, the suites that
called an interpreter only this machine has, the absent licence, the hosting that required
Windows, and a gate that had only ever been green in one place.

Four backlog entries came due at once, because the trigger each of them named was the same
sentence: a second machine.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | `install-hub.ps1`: the two failures found while writing `start-hub.ps1` | done | `c694232` |
| 2 | The smokes say which interpreter they could not find, and find the one that exists | done | `5ab2524` |
| 3 | `LICENSE`, and a README that says what the shared token does not protect | done | `be12b31` |
| 4 | `Dockerfile`: hosting the hub without Windows | done | `93b6773` |
| 5 | `.github/workflows/`: the gate on a machine that is not this one | done | `8c81cea` |

Verified at close (5 September 2026): `build` **0 warnings, 0 errors**; `test` **99 passed, 0
failed, 0 skipped**; `format --verify-no-changes` clean; `restore --force` clean, no advisory;
`bash scripts/test-all.sh` **four suites green, 66 checks**; the same suite **inside
`mcr.microsoft.com/dotnet/sdk:10.0`**, green; and the container itself driven by hand.

---

## What the plan got wrong

| The plan said | What was true |
|---|---|
| Phase 1 was moving two corrections that already existed | Where they live was the decision. Copying them across would leave two copies of a fix whose entire history is one script being repaired and the other not, so they became `scripts/ArcHost.ps1` and both scripts dot-source it |
| Phase 2 was a preflight that requires `python` — the fix the backlog named in advance | Requiring the name would have moved the failure to the right place and still failed: this machine has only `python`, and the Linux the gate now runs on has only `python3`. The preflight resolves the interpreter instead of demanding one |
| Phase 4 was writing a Dockerfile | Running it was the phase. Building it needed `/data` created before dropping to the `app` user; starting it printed a warning about `HTTP_PORTS` on every boot; and the hub's refusal to start without a token offered `$env:ARC_TOKEN = …`, a PowerShell line inside a Linux container |
| Phase 5 was adding a workflow | Rehearsing it inside the SDK image was the phase. `smoke-cli.sh` looked for `arc.exe`, and on Linux the binary is `arc` — the suite would have failed on the first run of the CI this increment exists to add |

The pattern is increment 05's, again and in four places: **the defect is in what was never run, not
in what was never read.** Every one of these was found in the minute after typing the command, and
none of them was findable by reading the file it lived in.

## What was decided

**[P015](../adr/P015-the-licence-is-mit.md)** — MIT. Apache-2.0's patent grant protects against a
risk this project does not have, and GPL would buy contributions back at the price of the adoption
this increment is for.

**Two files in `scripts/` are loaded rather than run.** `ArcHost.ps1` and `preflight.sh` each hold
what two or more scripts have to do identically, and each exists because that identical thing had
already drifted. The bar is written into
[testing.project.md](../../.claude/rules/testing.project.md) with what it does not authorise: the
counts, `check()` and `jget()` stay duplicated on purpose, because a shared file that grows into a
test framework is how a suite stops being readable on its own.

**CI is not the gate and does not stand in for it.** The Stop hook still is not in this repository
and still fails silently; the workflow answers minutes later and elsewhere. What it does that the
hook cannot is fail on a machine that is not this one, which is the only reason it was worth
adding.

**The README says what the shared token does not protect**, where the token is handed over, rather
than leaving it to be found in two decision records. Send references, not content was already the
channel's rule for its own reasons; it is also what keeps a channel with no confidentiality from
being where anything confidential lives.

## What this increment closed

Four entries left [backlog.md](../backlog.md) for this file:

* **`install-hub.ps1` had never been run in Windows PowerShell, and failed there twice.** Both
  corrections are in, and both were verified in PowerShell 5.1 — the token generates, and the
  address announced is the LAN's `192.168.2.53` rather than WSL's `172.22.160.1`.
* **The smokes reported a content mismatch when the problem was a missing interpreter.**
  `scripts/preflight.sh`, sourced by the four suites and by `test-all.sh`.
* **`LICENSE`.** MIT — [P015](../adr/P015-the-licence-is-mit.md).
* **CI.** `.github/workflows/gate.yml`, on `ubuntu-latest`, for every push to `master` and every
  pull request.

What did **not** close, and is now written down where it can be decided against: the service
installation itself still has not been run end to end, CI covers Linux only, and the image is not
published anywhere.

**Postscript, the same day.** The last of those four was that the workflow had never executed on
GitHub's own runners, only in a rehearsal of one. PR #6 and the merge into `master` ran it twice —
`gate` green both times, all four steps, 67 seconds — so it left the backlog before the increment
was a day old. The rehearsal had earned its keep by then: what it caught, `arc.exe` on Linux, would
have failed that first run.
