# Current work

**Nothing in progress.** Increments 01 to 10 are closed in [specs/](specs/).

**Last verified** (6 September 2026, at the close of increment 10):

* `dotnet build` — **0 warnings, 0 errors**, `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on
* `dotnet test` — **133 passed, 0 failed, 0 skipped**, 1 project
* `dotnet format --verify-no-changes` — clean
* `dotnet restore --force` — clean, no advisory
* every relative link in the four files this increment edited resolves, checked mechanically rather
  than by reading them
* the glob shapes added to two `paths:` blocks match what they are meant to, checked against git's
  own matcher — `.github/workflows` (which is how the harness stores `.github/workflows/**`, the
  trailing `/**` being stripped) matches `.github/workflows/gate.yml`, and `Dockerfile` matches
  the file

**What is verified and what is not.** Increment 10 changed no code, so the four smoke suites in
`scripts/test-all.sh` were **not** re-run: nothing here touches the wire, a surface or a script,
and increment 09's run of them stands. What is genuinely unverified is the one thing this increment
is about — **whether a rule now loads for a file that did not match before**. It cannot be checked
from inside the session that made the change, because a rule already opened by hand is not injected
again. The check is to open `.github/workflows/gate.yml` or the `Dockerfile` in a **fresh** session
and see `build-and-packages.project.md` arrive, or to count the loads in that session's transcript
as [P021](adr/P021-a-rule-that-never-arrived.md) describes. Until somebody does that, the routing
table in `CLAUDE.md` is the part that is known to work, because it is always in context.

Everything increment 06 verified about hosting — the container, the SDK image, Windows PowerShell
5.1, the installer's reachable half — was not re-run and is unaffected: nothing since has touched
how the hub is started. Its record stands in
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
