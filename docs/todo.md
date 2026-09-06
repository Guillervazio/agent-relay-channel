# Current work

**Nothing in progress.** Increments 01 to 11 are closed in [specs/](specs/).

**One thing to watch, and it is not verified.** Increment 11 copied `shared/architecture.md` and
`shared/build-and-packages.md` from `dotnet-house`'s `docs/a-base-that-did-not-survive-b`, which
was **not yet merged** when the copies were taken. If that branch changed before merging, these two
copies are of something that never shipped. Re-take them from the package's `master` at 0.3.0 and
diff; they should differ only in the `paths:` block of `api-guidelines` and in the rewritten
`H###` links, as every other copy here does.

**Last verified** (7 September 2026, at the close of increment 11):

* `dotnet build` — **0 warnings, 0 errors**; `dotnet test` — **133 passed, 0 failed, 0 skipped**;
  `dotnet format --verify-no-changes` clean. Increment 11 changed no code, so `test-all.sh` was
  not re-run
* the four `shared/` copies differ from the package's files only where a copy is allowed to differ,
  checked with `diff` rather than by reading: the adapted `paths:`, the rewritten record links, and
  in `architecture.md` two links to files that exist only in the package
* every relative link in the edited files resolves, checked mechanically
* the appendix repeats no clause the base now carries — three passages moved out, and what remains
  was read against the base rather than assumed

**At the close of increment 10** (6 September 2026):

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

**The check increment 10 could not run has now been run** (7 September 2026, in a fresh session,
which is the only place it can be run — a rule already opened by hand is not injected again).
Opening the `Dockerfile` brought `build-and-packages.project.md`; opening
`.github/workflows/gate.yml` brought `testing.project.md`; opening `ChannelService.cs` brought the
five it should and not `protocol.project.md`, which does not cover that file. Seven rule files over
three reads, none opened by hand. The routing table was also obeyed where it costs something: the
SDK bump was refused as a decision needing approval, and the `limit` on the inbox was refused by
name.

What the run found is in the same session's transcript and is fixed here: two rows of that table
said *base and appendix* for paths the base cannot carry, which is a precondition promising
something it cannot deliver. Split in two, with what actually arrives named.

Increment 10 changed no code, so the four smoke suites in `scripts/test-all.sh` were **not** re-run
and increment 09's run of them stands.

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
