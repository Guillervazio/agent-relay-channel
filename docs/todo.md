# Current work

**Nothing in progress.** Increments 01 to 09 are closed in [specs/](specs/).

**Last verified** (6 September 2026, at the close of increment 09):

* `dotnet build` — **0 warnings, 0 errors**, `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on
* `dotnet test` — **133 passed, 0 failed, 0 skipped**, 1 project
* `dotnet format --verify-no-changes` — clean
* `dotnet restore --force` — clean, no advisory
* `bash scripts/test-all.sh` — **four suites green, 114 checks** (45 REST, 28 CLI, 30 MCP, 11 UI)
* the rules reconciled against the change rather than only where it edited: six clauses checked,
  three rewritten, the costliest in a file this increment never opened —
  `api-guidelines.project.md` said the inbox needs no caller-supplied bound because it is one
  agent's mailbox, which read literally forbade the fix
* by hand against a running hub on `:8803`, reading the answers rather than their status codes: a
  delivered notice came back through `?replay=60` with its body and accents intact and
  `status: "delivered"`, came back again unchanged on a second call, and never returned to the
  default mailbox; `replay=90000` and `replay=-5` answered `invalid_replay` naming the range on
  all three surfaces; the CLI exited 4, 4, 0 and 1 across `inbox`, `--unanswered`, `--replay 60`
  and `--replay 90000`

Everything increment 06 verified about hosting — the container, the SDK image, Windows PowerShell
5.1, the installer's reachable half — was not re-run and is unaffected: this increment changed what
the mailbox will hand back, and touched nothing about how the hub is started. Its record stands in
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

* **The two halves of the delivery finding that increment 09 deliberately left.** Two polls by the
  same agent both receive the same messages, and a message is marked delivered before the client
  has it — including reporting `status: "pending"` for a row that is already `delivered`. They are
  one change, not two: the inbox read and its delivery marking belong in one transaction, and that
  is also what fixes the duplicate. Increment 09 added a way out of the consequence
  ([P020](adr/P020-a-recovery-window-not-a-state.md)) and repaired nothing about the cause, which
  is why this is now the nearest thing due.
* **The service installation has still never been run end to end.** Everything past
  `install-hub.ps1`'s administrator check is unexecuted. Due the next time the hub is installed as
  a service, which is also the first time somebody follows the README to the end.
* **`MessageStatus.Expired` is published and never produced.** The last of the three "published and
  unreachable" defects, and the only one that could not be closed in increment 08: removing a value
  of `MessageStatus` is breaking, so it waits for a `/v2`. P020 is explicit that the 86400-second
  cap is not a retention policy and does not bring anything closer to expiring.
