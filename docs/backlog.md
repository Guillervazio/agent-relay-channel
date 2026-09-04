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
| **`GET /v1/messages/{id}` and `GET /v1/threads/{id}` perform no authorisation at all.** Any agent holding the token reads any message body, given an id | It defeats the 403 that `InboxAsync` raises, and it is the reason [P006](adr/P006-403-on-another-agents-mailbox.md) has to say the confidentiality H011 protects does not currently exist here. Ids are 64 random bits, so this is obscurity, not access control — and an id is handed out in every thread listing. **Due when:** before the channel carries anything one agent must not read. It is not "found and deferred on merit"; it is found and named |
| **`shared/architecture.md` did not survive its second consumer.** ARC would need to deviate from five clauses: the five-role graph, what each role must not contain, aggregate-root repositories, vertical slices, and handler-based CQRS | By `dotnet-house`'s own criterion, two or more deviations means the base was a project decision in disguise. ARC wrote a standalone [architecture.project.md](../.claude/rules/architecture.project.md) instead. **Due when:** report it to the package. Do not fix it from inside this repository — the base is the package's |
| **Three `H###` records link to `P###` records that do not travel with them.** H007, H011 and H013 each end with an `## Origin` line pointing at a `P` in `PlastipackInventoryApp` | A portable record linking to a non-portable one is the "each fact has one home" rule failing at the seam: in this repository those three links resolved to nothing, and `P007`, `P008` and `P011` here are entirely different decisions, so the link was not merely dead but misleading. The copies here were flattened to plain text naming the source project. **Due when:** report it to `dotnet-house`; the fix is a commit there, and it is the package's to make |
| **Two mailbox polls by the same agent both receive the same messages.** `Signal` wakes every waiter on a key, and the key is the agent name; both re-read and both return the rows before either marks them delivered | The `UPDATE`'s `status = 'pending'` keeps the database coherent, so only one marks — but both HTTP responses carry the message. Duplicate delivery, not loss. Reachable by leaving two terminals waiting, which is the described usage. **Due when:** an agent reports handling a message twice, or the inbox read and its delivery marking are put in one transaction for another reason |
| **A message is marked delivered before the client has it.** `InboxAsync` marks and then returns; a response lost in transit takes the messages out of the default mailbox | A `request` is recoverable with `?unanswered=true`. **A `note` is not recoverable at all** — the recovery query is restricted to requests — so a delivered notice whose response was lost is gone. Returned messages also carry `status: "pending"` although the row is already `delivered`. **Due when:** a note goes missing, or the channel is used over a link where a dropped response is expected |
| **`invalid_refs` promises a validation nothing performs, and REST cannot emit it.** Nothing checks that `refs` is a JSON *object*; over REST a malformed `refs` fails the whole body parse and comes back as `invalid_json` 400 | The code is published, frozen by a test, and its only throw site (`ArcTools.ParseRefs`) is never executed by any suite. `refs` is also unbounded while `body` is capped at 256 KB, so a 400 KB `refs` is accepted where a 257 KB body is refused. **Due when:** deciding what `refs` is. Either check `ValueKind` in `ChannelService` so all three surfaces share it, or correct the contract to say any JSON value — the two must not stay disagreeing |
| **A malformed `--refs` is discarded silently and the message is sent anyway.** `Arc.Cli.ReadRefs` writes to stderr and returns `null`, which the caller cannot tell from "no refs given" | The agent believes it sent the branch and commit. It did not, and the exit code says `0`. `--refs-file` naming a missing file takes the same path, where `ReadBody` in the same file checks existence and fails. **Due when:** now, on merit. It is the one finding here whose fix is a handful of lines |
| **`note` to oneself is allowed; `ask` is not.** `AskAsync` refuses with `self_addressed`; `NoteAsync` has no such check | Two sibling operations differ with no comment, rule or record saying why. Whichever way it is settled, one of the two is currently wrong. **Due when:** somebody needs the answer. Note that forbidding it would be a new refusal on a published route |
| **`MessageStatus.Expired` is published and never produced.** The enum has four values, the code writes three, and the observer panel already carries a label for the fourth | Changing a value of `MessageStatus` is breaking per [protocol.project.md](../.claude/rules/protocol.project.md), so the contract carries a state that means nothing. **Due when:** either something starts expiring messages, or the value is removed — and removing it is the breaking change, so it waits for a `/v2` |
| **The smokes report a content mismatch when the problem is a missing interpreter.** `jget()` calls `python`, not `python3`, and swallows stderr with `2>/dev/null` | On this machine `python3` does not exist and `python` resolves to a conda environment on `PATH` by accident. A missing interpreter yields an empty string and the comparison fails talking about the wrong thing. **Due when:** the smokes run on a second machine, or `test-all.sh` enters CI. The fix, named in advance: a `require python` preflight that exits 1 with a sentence |

---

## Known limitations

Accepted knowingly. Each is a thing the system does not do, written down so it does not get
rediscovered as a bug.

- **The agent name is not a credential.** Any holder of the token can present any name —
  [P004](adr/P004-one-token-and-an-agent-header.md). The 403 stops a mistake and a curious agent,
  never a dishonest one.
- **One hub, one file.** [P003](adr/P003-sqlite-on-a-file.md) assumes a single process owning the
  database. Two hubs over a share is not supported and is not merely untested.
- **`WaiterRegistry` and `Arc.Cli` still read the real clock.** Everything the channel *writes* now
  dates itself from an injected `TimeProvider`, but the registry waits on `Task.Delay` and the CLI
  measures elapsed time for its progress line, so `WaiterRegistryTests` still measures real
  milliseconds and is the part of the suite most exposed to a slow machine.
  <br>**Due when:** that suite fails on timing, or the CLI gets the composition root it has none of
  today — the registry's case is a change to the wait mechanism, not to a timestamp.
- **The schema cannot change destructively.** `CREATE … IF NOT EXISTS` silently does nothing
  against an older table — [P007](adr/P007-the-schema-is-created-at-startup.md).

---

## What is left, and what would make each of them due

### Waiting on evidence, not on effort

- **CI.** There is no `.github/workflows/`. With one contributor and a Stop hook that blocks a
  turn on a red build, CI would re-run what already ran on the only machine that matters.
  <br>**Due when:** a second person can push, or a release is cut from a machine that is not this
  one.

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

- **Three of the four surfaces have no unit test at all.** `Arc.Hub/Program.cs`,
  `Arc.Hub/ArcTools.cs` and `Arc.Cli/Program.cs` are 1,144 lines with zero xunit coverage: the
  401, the `bad_agent` 422, the 200-versus-202 mapping, the empty mailbox's 204 and every MCP
  tool are exercised only by `scripts/test-all.sh`, which no gate runs. `EventStream` has none
  either, and `ChannelServiceTests` builds the service **without** one, so the publish branches
  never execute — which leaves `PROTOCOL.md`'s `delivered` event, its two-second `: ping` and its
  `DropOldest` promise asserted by nothing anywhere.
  <br>**Due when:** a defect reaches a commit in one of those files, or the smokes stop being run
  by hand. Note that the hub and CLI are top-level statements with no seam, so this is a design
  question before it is a testing one.

- **Nothing exercises a wait past the derived `KeepAliveTimeout`.** The longest smoke waits 60
  seconds against a keep-alive of `ARC_MAX_WAIT + 60`.
  <br>**Due when:** `ARC_MAX_WAIT` changes, or an agent reports a wait cut short around two
  minutes.

- **`Arc.Cli` inherits SQLite for four records** — [P012](adr/P012-the-cli-takes-sqlite-with-it.md).
  <br>**Due when:** the published CLI's size or dependency surface is questioned. Measure first:
  `dotnet publish src/Arc.Cli -c Release -r win-x64`, with and without.

### Housekeeping, on its own clock

- **The GitHub remote.** The repository is local-only: `gh` is not installed and neither is
  `winget`, so the private remote was not created.
  <br>**Due when:** the repository is created on github.com and its URL is known. Then
  `git remote add origin <url>` and `git push -u origin master`.

- **The denylist in `.claude/settings.json`.** It reads
  `Bash(rm * demo/arc.db*)`, which is a pattern nobody has seen match anything. The intended
  entry is the service uninstaller — `*install-hub.ps1*-Uninstall*` — the way Plastipack denies
  `dotnet ef database drop`.
  <br>**Due when:** now, by hand. A denylist that does not match is worse than none: it reads as
  protection that is not there.

- **The demo token.** `demo/token.txt` and the two real `.mcp.json` are gitignored and were never
  committed, so this is about rotation and not about history.
  <br>**Due when:** the demo runs against a hub reachable off loopback.

- **`LICENSE`.** There is none.
  <br>**Due when:** the repository stops being private, or somebody outside asks to use it.

- **The `SQLitePCLRaw` pin** — [P008](adr/P008-the-sqlitepclraw-pin.md). Read
  `Microsoft.Data.Sqlite`'s nuspec, **not** `dotnet list package --vulnerable`, which is clean
  because of the pin.
  <br>**Due when:** a `Microsoft.Data.Sqlite` release declares a fixed dependency. Check it when
  upgrading anything, and drop the pin in the commit that no longer needs it.

- **`P013` is a promotion candidate.** `PlastipackInventoryApp` took the same decision
  independently as its P012, which is the second-project condition the package sets for promoting
  a `P` to an `H`.
  <br>**Due when:** the package's owner decides. It is a commit in `dotnet-house` first.
