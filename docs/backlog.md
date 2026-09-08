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
| **The four smoke suites pass unchanged over a changed wire.** Increment 12 moved `status` on a first inbox read from `pending` to `delivered`, and 114 checks across REST, CLI, MCP and UI noticed nothing: not one of them asserts the status of a message it has just read | The suites check that a message arrives, with its body and its ids, and never what state it arrives in. So the one field that says what the mailbox *did* is unasserted on every surface, and it was verified by hand instead. This is the shape of every defect found twice here — a green suite over behaviour nobody wrote an assertion for. **Due when:** the next change to a message's state reaches the wire, or somebody adds the assertion to `smoke.sh` first, which is a few lines and closes it |
| **A message is marked delivered before the client has it.** `ClaimInboxAsync` marks and returns in one transaction; a response lost in transit still takes the messages out of the default mailbox | Increment 12 closed the two halves that were repairable without changing what the channel is: two simultaneous polls no longer both receive a message, and a first read no longer reports `pending` for a row it just set to `delivered` ([P023](adr/P023-the-mailbox-is-claimed-not-read.md)). What remains is the half that is not a defect in the code but a property of the design: nobody tells the hub the message arrived. `?replay=N` ([P020](adr/P020-a-recovery-window-not-a-state.md)) is the way back and not a repair. **Due when:** a client is willing to acknowledge — which is a change to [P001](adr/P001-long-polling-not-a-broker.md) and needs its own record, not an extension of this one |
| **The service installation has still never been run end to end.** Increment 06 fixed the two failures `install-hub.ps1` had in Windows PowerShell and verified them in that console — the token generates, the address is the LAN's — but `sc.exe create`, the machine-level variables and the firewall rule need an administrator and change this machine, so nothing has executed them | The script's own two defects are gone; what is unverified is everything after its administrator check. Somebody following the README to the end is still the first to run it. **Due when:** the hub is next installed as a service, or a second person adopts it that way |
| **`MessageStatus.Expired` is published and never produced.** The enum has four values, the code writes three, and the observer panel already carries a label for the fourth | Changing a value of `MessageStatus` is breaking per [protocol.project.md](../.claude/rules/protocol.project.md), so the contract carries a state that means nothing. **Due when:** either something starts expiring messages, or the value is removed — and removing it is the breaking change, so it waits for a `/v2` |
| **A unit test fails rarely, and it is `ArcToolsTests.Un_hilo_devuelve_la_conversacion_entera`.** First seen in increment 08 with the name unrecoverable; seen again on 7 September 2026, in the first of three consecutive full-suite runs during increment 12 | The name is the whole of what the second occurrence bought, and it was the point of keeping the entry. It did not reproduce afterwards: six runs of `ArcToolsTests` alone and eight consecutive full-suite runs, all green. So what is known is the test, not the cause — and `WaiterRegistryTests`, the standing suspect since increment 08, is not it. **Due when:** it happens a third time, which now has a name to be attached to. Capture the failure message: neither occurrence has one |
| **Nobody has opened a turn yet.** [P024](adr/P024-who-supplies-the-turn.md) settles who opens one and the demo is written for it, but no session has executed the arrangement: it needs a second CLI installed and a real turn command, and this increment deliberately produced neither | What is verified is the shape and the documents, which is the same standing the service installation has had since increment 06. What is unverified is everything the turn command touches: whether a one-turn invocation returns when the turn ends rather than hanging, whether the counterpart's thirty-second mailbox wait is margin enough, and what a turn command that fails looks like from the lead's side — today it is indistinguishable from a counterpart that is simply slow. **Due when:** somebody runs the demo, or a real session opens a turn for the first time |

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

- **An answer reaches the agent who asked for it twice.** `arc await` hands the response to the
  waiter without marking it, so the row stays `pending` and that same agent's mailbox delivers it
  again afterwards, that time as `delivered`. This contradicts nothing written down:
  [PROTOCOL.md](PROTOCOL.md) says `delivered` happens *when read from the mailbox*, and `await` does
  not read the mailbox. It also has a defensible reading — a response lost on its way out of
  `await` would otherwise have no way back, which is [P020](adr/P020-a-recovery-window-not-a-state.md)'s
  own argument. What does not exist is the decision: nothing says the second delivery should happen
  and nothing asserts that it does. Seen on 7 September 2026 between `claude-a` (Claude Code) and
  `codex-b` (Codex CLI) against a running hub, which is also the first time two providers used this
  channel for its purpose rather than a suite driving it.
  <br>**Due when:** an agent reports handling one answer twice, or somebody writes the first
  assertion about a response's status — which is the same line `smoke.sh` is missing for the
  finding above.

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
- **Only the lead can start an exchange.** [P024](adr/P024-who-supplies-the-turn.md) has the lead
  open the counterpart's turn when it needs an answer, so a counterpart that thinks of something
  after its turn ended has no way to say it until it is needed again. It is a narrowing taken
  knowingly, not an oversight.
  <br>**Due when:** a case genuinely needs both sides to initiate. The answer is then the
  background loop P024 names and declines, on its own record — not a widening of this one.
- **A deadlock now happens with nobody in front of a console.** Two agents each waiting on the
  other was always possible, and until now a person was watching when it did. The handshake still
  says never both wait at once and `waiters` on `/healthz` still shows it, but nothing acts on
  either.
  <br>**Due when:** a session deadlocks, or the lead is given something that reads `waiters` and
  gives up. Note that reading the *mailbox* to detect it is not the answer: a read claims what it
  finds ([P023](adr/P023-the-mailbox-is-claimed-not-read.md)), so a watcher would take delivery of
  messages on behalf of an agent that has not seen them. `/v1/observe/stream` marks nothing and is
  the one that can be watched safely.

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
