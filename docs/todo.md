# Current work

**Nothing in progress.** Increments 01 to 08 are closed in [specs/](specs/).

**Last verified** (5 September 2026, at the close of increment 08):

* `dotnet build` — **0 warnings, 0 errors**, `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on
* `dotnet test` — **119 passed, 0 failed, 0 skipped**, 1 project
* `dotnet format --verify-no-changes` — clean
* `dotnet restore --force` — clean, no advisory
* `bash scripts/test-all.sh` — **four suites green, 95 checks** (37 REST, 22 CLI, 25 MCP, 11 UI)
* the rules reconciled against the change rather than only where it edited: six clauses in five
  files, the costliest in one this increment never touched
* by hand against a running hub, reading the answers rather than their status codes: `arc_note`
  with broken `refs` came back as `invalid_refs: 'refs' debe ser JSON válido: …`, and `arc_ask` to
  oneself with `wait: 5` as `self_addressed: Un agente no puede esperar su propia respuesta…`.
  Both had answered `An error occurred invoking 'arc_x'.` on the same hub before the filter —
  which is how that finding was found at all

**One thing this close cannot claim.** A unit test failed once under `test-all.sh` during this
increment and has not reproduced in the twenty-odd runs since. Its name is unrecoverable because
the suite piped `dotnet test` through `tail -3`; that pipe is gone. It is in
[backlog.md](backlog.md) so the next occurrence is the second one.

Everything increment 06 verified about hosting — the container, the SDK image, Windows PowerShell
5.1, the installer's reachable half — was not re-run and is unaffected: this increment changed what
the channel accepts and how MCP reports a refusal, and touched nothing about how the hub is
started. Its record stands in
[specs/06-a-machine-that-is-not-this-one.md](specs/06-a-machine-that-is-not-this-one.md).

---

## Opening the next one

This file becomes the plan: what the increment is for, and one row per phase with its commit.
**Every phase is closed in the same commit that updates its row**, and the increment is closed by
moving the narrative to [specs/](specs/) and leaving this file as it is now.

## What is closest to due

Nothing is committed to, and this is not a ranking — it is where [backlog.md](backlog.md) says a
trigger is nearest, so the next decision is taken against something rather than from a blank page.
Each entry there names what has to become true first; read those rather than this list.

* **Three findings about delivery**, all reachable by the usage the README describes: two polls by
  the same agent both receive the same messages, a message is marked delivered before the client
  has it, and a lost `note` is unrecoverable because `?unanswered=true` covers only requests. Now
  the oldest group still open by a wide margin, and the only one whose trigger is ordinary use
  rather than a decision.
* **The service installation has still never been run end to end.** Everything past
  `install-hub.ps1`'s administrator check is unexecuted. Due the next time the hub is installed as
  a service, which is also the first time somebody follows the README to the end.
* **`MessageStatus.Expired` is published and never produced.** The last of the three "published and
  unreachable" defects, and the only one that could not be closed in increment 08: removing a value
  of `MessageStatus` is breaking, so it waits for a `/v2`.
