# Backlog

What is still owed, what would make each of it due, and what this repository knows to be wrong.
Nothing here is in progress.

**An entry with no trigger written down is an entry nobody can decide against.** Every entry below
names what has to become true first.

What is finished lives in [specs/](specs/), one file per increment. What is being worked on now is
in [todo.md](todo.md).

---

## Findings that contradict the docs

Recorded as they are hit. A finding leaves this file for the spec of the increment that closed it,
so what is listed here is what is still true and still wrong.

| Finding | Impact |
|---|---|
| **`shared/architecture.md` did not survive its second consumer.** ARC would need to deviate from five clauses: the five-role graph, what each role must not contain, aggregate-root repositories, vertical slices, and handler-based CQRS | By `dotnet-house`'s own criterion, two or more deviations means the base was a project decision in disguise. ARC wrote a standalone [architecture.project.md](../.claude/rules/architecture.project.md) instead. **Due when:** report it to the package. Do not fix it from inside this repository — the base is the package's |
| **Three `H###` records link to `P###` records that do not travel with them.** H007, H011 and H013 each end with an `## Origin` line pointing at a `P` in `PlastipackInventoryApp` | A portable record linking to a non-portable one is the "each fact has one home" rule failing at the seam: in this repository those three links resolved to nothing, and `P007`, `P008` and `P011` here are entirely different decisions, so the link was not merely dead but misleading. The copies here were flattened to plain text naming the source project. **Due when:** report it to `dotnet-house`; the fix is a commit there, and it is the package's to make |
| **Two mailbox polls by the same agent both receive the same messages.** `Signal` wakes every waiter on a key, and the key is the agent name; both re-read and both return the rows before either marks them delivered | The `UPDATE`'s `status = 'pending'` keeps the database coherent, so only one marks — but both HTTP responses carry the message. Duplicate delivery, not loss. Reachable by leaving two terminals waiting, which is the described usage. **Due when:** an agent reports handling a message twice, or the inbox read and its delivery marking are put in one transaction for another reason |
| **A message is marked delivered before the client has it.** `InboxAsync` marks and then returns; a response lost in transit takes the messages out of the default mailbox | The marking is still in the wrong order, and returned messages still carry `status: "pending"` on that first read although the row is already `delivered`. What is no longer true is that the loss is permanent: increment 09 added `?replay=N` ([P020](adr/P020-a-recovery-window-not-a-state.md)), so a notice whose response was lost comes back within the window. That is a way out, not a repair — a message is still handed over before anyone knows it arrived. **Due when:** the inbox read and its delivery marking are put in one transaction, which is the same change the duplicate-delivery entry above needs, or somebody depends on `status` being accurate on a first read |
| **The service installation has still never been run end to end.** Increment 06 fixed the two failures `install-hub.ps1` had in Windows PowerShell and verified them in that console — the token generates, the address is the LAN's — but `sc.exe create`, the machine-level variables and the firewall rule need an administrator and change this machine, so nothing has executed them | The script's own two defects are gone; what is unverified is everything after its administrator check. Somebody following the README to the end is still the first to run it. **Due when:** the hub is next installed as a service, or a second person adopts it that way |
| **`MessageStatus.Expired` is published and never produced.** The enum has four values, the code writes three, and the observer panel already carries a label for the fourth | Changing a value of `MessageStatus` is breaking per [protocol.project.md](../.claude/rules/protocol.project.md), so the contract carries a state that means nothing. **Due when:** either something starts expiring messages, or the value is removed — and removing it is the breaking change, so it waits for a `/v2` |
| **A unit test failed once and nothing knows which.** It happened during increment 08 under `test-all.sh`, and has not reproduced in the twenty-odd runs since — including four full `test-all.sh` passes, six suite runs under the same environment variables, and eight runs of `WaiterRegistryTests` alone, which was the obvious suspect and is not confirmed | The name is unrecoverable: the suite piped `dotnet test` through `tail -3`, which keeps the count and drops it. That pipe is gone in `67599c3`, so the next occurrence prints what failed and keeps the log in `./unit-fail.log`. **Due when:** it happens again — and this entry exists so that occurrence is the second one and not the first. `WaiterRegistryTests` measuring real milliseconds is the standing candidate, recorded under *Known limitations* below |

---

## Known limitations

Accepted knowingly. Each is a thing the system does not do, written down so it does not get
rediscovered as a bug.

- **The agent name is not a credential.** Any holder of the token can present any name —
  [P004](adr/P004-one-token-and-an-agent-header.md). The 403 stops a mistake and a curious agent,
  never a dishonest one.
- **The observer reads the whole channel, and the token is all it asks for.**
  `/v1/observe/history` serves every message with its body and does not even require
  `X-ARC-Agent`. That is what the panel is — [P010](adr/P010-the-observer-page-is-unauthenticated.md)
  — and it is why the 404 on somebody else's message
  ([P016](adr/P016-a-message-is-read-by-its-two-ends.md)) is a guardrail against a mistake and not
  a boundary. `smoke.sh` and `HubEndpointTests` both assert it still works, so it cannot quietly
  disappear as a side effect of tightening something else either.
  <br>**Due when:** the token is held by somebody who must not read every conversation. The answer
  is then not a scoped observer — it is per-agent credentials, which is what
  [P004](adr/P004-one-token-and-an-agent-header.md) says has to change first.

- **One hub, one file.** [P003](adr/P003-sqlite-on-a-file.md) assumes a single process owning the
  database. Two hubs over a share is not supported and is not merely untested.
- **`WaiterRegistry` and `Arc.Cli` still read the real clock.** Everything the channel *writes* now
  dates itself from an injected `TimeProvider`, but the registry waits on `Task.Delay` and the CLI
  measures elapsed time for its progress line, so `WaiterRegistryTests` still measures real
  milliseconds and is the part of the suite most exposed to a slow machine.
  <br>**Due when:** that suite fails on timing, or the CLI gets the composition root it has none of
  today — the registry's case is a change to the wait mechanism, not to a timestamp. Increment 08
  saw one unrecorded unit failure and could not name it; this suite is the standing suspect and
  eight consecutive runs did **not** reproduce anything, so it is a suspect and not a finding.
- **CI covers Linux only.** `.github/workflows/gate.yml` runs on `ubuntu-latest`; the machine
  that develops this is the only thing that ever exercises Windows, and it does so by hand. A
  Windows-specific regression reaches `master` unseen unless somebody runs the suite here first —
  which the Stop hook does, when it is running.
  <br>**Due when:** a second contributor pushes from a platform that is not Windows, or a defect
  that only appears on Windows reaches `master`.
- **The container image is not published anywhere.** Adopting the hub by container means cloning
  the repository and building it.
  <br>**Due when:** somebody asks for an image, or a release is cut that is meant to be installed
  without a clone.
- **The schema cannot change destructively.** `CREATE … IF NOT EXISTS` silently does nothing
  against an older table — [P007](adr/P007-the-schema-is-created-at-startup.md).
- **Pull requests are opened by hand.** `origin` is
  `github.com/Guillervazio/agent-relay-channel` and branches merge back through it, but `gh` is
  not installed and neither is `winget`, so nothing here can open, review or merge one. A branch
  is pushed from the command line and its PR is opened in the browser. This entry replaces one
  that said the repository was local-only, which stopped being true at PR #2.

---

## What is left, and what would make each of them due

### Waiting on evidence, not on effort

- **A coverage number.** `coverlet.collector` is installed and nothing reads its output. The
  testing base deliberately sets no percentage, so adding a threshold would contradict a rule this
  repository just adopted.
  <br>**Due when:** somebody asks a coverage question. Then either a report is produced or the
  package is removed — carrying a collector nobody collects from is the worst of the three states.

- **`AnalysisLevel` / `AnalysisMode`.** Left where the SDK puts them. Measured on 4 September 2026:
  at the default mode, **no CA rule fires anywhere in the solution**, so raising it is a change
  whose effect is unknown and whose benefit is unmeasured.
  <br>**Due when:** the first CA-class defect reaches a commit.

- **`packages.lock.json` / `nuget.config`.** Restores are reproducible in practice because there is
  one machine.
  <br>**Due when:** a restore on a second machine resolves a different version than this one.

- **The observer's stream is still only covered by a bash script.** Increment 04 gave the three
  surfaces xunit coverage, but `/v1/observe/stream` is not among it: `HubEndpointTests` asserts the
  routes that answer and return, and the SSE endpoint answers by not returning. `smoke-ui.sh`
  drives it against a real hub — the `message` and `state` events, and a body arriving intact —
  and `EventStreamTests` covers the queue behind it, but nothing in the fast gate asserts the
  two-second `: ping` or that a dropped connection disposes its subscription.
  <br>**Due when:** a change to the stream's framing or its heartbeat, or an observer reporting a
  stall. The shape it needs is a test that reads a bounded prefix of the response body rather than
  awaiting the whole of it.

- **Nothing exercises a wait past the derived `KeepAliveTimeout`.** The longest smoke waits 60
  seconds against a keep-alive of `ARC_MAX_WAIT + 60`.
  <br>**Due when:** `ARC_MAX_WAIT` changes, or an agent reports a wait cut short around two
  minutes.

- **`Arc.Cli` inherits SQLite for four records** — [P012](adr/P012-the-cli-takes-sqlite-with-it.md).
  <br>**Due when:** the published CLI's size or dependency surface is questioned. Measure first:
  `dotnet publish src/Arc.Cli -c Release -r win-x64`, with and without.

### Housekeeping, on its own clock

- **The demo token.** `demo/token.txt` and the two real `.mcp.json` are gitignored and were never
  committed, so this is about rotation and not about history.
  <br>**Due when:** the demo runs against a hub reachable off loopback.

- **The `SQLitePCLRaw` pin** — [P008](adr/P008-the-sqlitepclraw-pin.md). Read
  `Microsoft.Data.Sqlite`'s nuspec, **not** `dotnet list package --vulnerable`, which is clean
  because of the pin.
  <br>**Due when:** a `Microsoft.Data.Sqlite` release declares a fixed dependency. Check it when
  upgrading anything, and drop the pin in the commit that no longer needs it.

- **`P013` is a promotion candidate.** `PlastipackInventoryApp` took the same decision
  independently as its P012, which is the second-project condition the package sets for promoting
  a `P` to an `H`.
  <br>**Due when:** the package's owner decides. It is a commit in `dotnet-house` first.
