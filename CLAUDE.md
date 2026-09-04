# CLAUDE.md

**ARC — Agent Relay Channel.** A hub on the LAN that lets CLI agents from different providers ask
each other questions and block until answered. A CLI agent only exists during its turn, so the
channel is HTTP long polling, not a broker. .NET 10, SQLite, three surfaces over one service.

---

## Commands

```bash
dotnet build                       # build the solution
dotnet test                        # the 49 tests; no container, no network
dotnet format                      # apply .editorconfig
dotnet run --project src/Arc.Hub   # the hub on :8765 (needs ARC_TOKEN, or ARC_ALLOW_ANONYMOUS=1)

bash scripts/test-all.sh           # end to end: hub on :8791 + the four smokes
```

`test-all.sh` needs `curl` and **`python`** — not `python3`, which does not exist on the machine
this was written on. `jget()` swallows stderr, so a missing interpreter surfaces as a content
mismatch about something else. See [docs/backlog.md](docs/backlog.md).

---

## Dependency direction

```
Arc.Cli  → Arc.Core
Arc.Hub  → Arc.Core
Arc.Core → the BCL and Microsoft.Data.Sqlite, nothing else
```

`Arc.Core` never references a surface, and no surface references another.

---

## Settled

`net10.0`. **No new NuGet package without explicit approval**, however useful it seems — approved,
rejected and pinned are listed in
[build-and-packages.project.md](.claude/rules/build-and-packages.project.md).

Settled with their reasoning in [docs/adr/](docs/adr/): long polling rather than a broker; one
logic in `ChannelService` with REST, MCP and the CLI as facades; SQLite on a file; one shared
token with the agent name as attribution and never authorisation; the identifier scheme; 403 on
another agent's mailbox; the schema created at startup; the `SQLitePCLRaw` pin; the CLI's exit
codes as contract; the unauthenticated observer page. Do not re-litigate them; read the record if
you need the argument.

---

## Where the binding specifications live

[.claude/rules/](.claude/rules/), loaded automatically when a matching file is read, and binding
whether or not they are quoted back.

Four areas are two files sharing one `paths:` — `shared/<area>.md` is the portable base, copied
from `dotnet-house` and edited **there**; `<area>.project.md` is ours. A deviation from a base
clause goes under `## Deviations` in that appendix, naming the clause it replaces, and **that
entry wins**. Those areas: `coding-conventions`, `testing`, `build-and-packages`, `api-guidelines`.

Four more have **no base**, and say so in their first line: `architecture`, `protocol`,
`persistence`, `concurrency`. The first replaces a base that did not survive contact with this
project; the other three cover what ARC is and the package has nothing on.

Each fact has one home; a rule that needs a fact from elsewhere links to it rather than restating
it. The long reasoning is not in the rules at all — it is in [docs/adr/](docs/adr/), read on
demand.

---

## Working here

* Starting a change is the `feature-workflow` skill; finishing one is `close-increment`; after
  deciding something the rules did not anticipate, `reconcile-rules`. They live in the
  `dotnet-house` plugin, with both hooks and both subagents — **not here**. `ef-migration` is also
  in that plugin and has **no subject in this repository**: there is no ORM.
* Branches are `feature/…`, `docs/…` or `refactor/…`, off `master` and merged back by PR. Commit
  messages are in the imperative and say **why**, not what the diff already shows.
* **No new architectural patterns.** Reuse what already exists. `Arc.Core` has zero interfaces,
  and the first one needs [H002](docs/adr/house/H002-single-implementation-interfaces.md)'s test.
* No TODOs, no commented-out code, no placeholders. Deferred work goes in
  [docs/backlog.md](docs/backlog.md).
* A change to the wire changes [docs/PROTOCOL.md](docs/PROTOCOL.md) in the same commit.
* If a requirement is ambiguous or several designs are defensible, ask before choosing.

**A green turn is not evidence the gate ran.** It arrives as a plugin, and every way it can stop
running is silent — see
[testing.project.md](.claude/rules/testing.project.md#nothing-in-this-repository-runs-the-gate).

Current work: [docs/todo.md](docs/todo.md). Finished increments: [docs/specs/](docs/specs/).
