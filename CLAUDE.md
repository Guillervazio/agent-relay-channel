# CLAUDE.md

**ARC — Agent Relay Channel.** A hub on the LAN that lets CLI agents from different providers ask
each other questions and block until answered. A CLI agent only exists during its turn, so the
channel is HTTP long polling, not a broker. .NET 10, SQLite, three surfaces over one service.

---

## Commands

```bash
dotnet build                       # build the solution
dotnet test                        # the 133 tests; no container, no network
dotnet format                      # apply .editorconfig
dotnet run --project src/Arc.Hub   # the hub on :8765 (needs ARC_TOKEN, or ARC_ALLOW_ANONYMOUS=1)

./scripts/start-hub.ps1            # the same hub, with the token and the URLs worked out for you
./scripts/start-hub.ps1 -Lan       # …listening on the network, saying what the LAN still needs

bash scripts/test-all.sh           # end to end: hub on :8791 + the four smokes

docker build -t arc-hub .          # the hub with neither the SDK nor PowerShell on the host
```

`test-all.sh` needs `curl` and **Python 3.7 or later under either name**: the scripts try
`python3` and then `python`, because this machine has only the second and a Linux runner usually
only the first. Anything they need and cannot find, they name before doing any work —
[scripts/preflight.sh](scripts/preflight.sh).

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
another agent's mailbox but 404 on somebody else's message; the schema created at startup; the `SQLitePCLRaw` pin; the CLI's exit
codes as contract; the unauthenticated observer page; the channel explaining itself in the MCP
handshake rather than in every repository that adopts it; recovering a delivered message with a
window rather than a state, which writes nothing; MIT as the licence; this page routing to the
rules because their loading is conditional and fails silently. Do not re-litigate them; read the
record if you need the argument.

---

## Where the binding specifications live

[.claude/rules/](.claude/rules/), binding whether or not they are quoted back — and, as the next
section says, not in front of you until you put them there.

Four areas are two files — `shared/<area>.md` is the portable base, copied from `dotnet-house` and
edited **there**; `<area>.project.md` is ours. A deviation from a base clause goes under
`## Deviations` in that appendix, naming the clause it replaces, and **that entry wins**. Those
areas: `coding-conventions`, `testing`, `build-and-packages`, `api-guidelines`.

The appendix may carry `paths:` the base does not, and two of them do: a path that exists only in
this project cannot be added to a file edited in another repository. Where they differ, opening
such a file loads the appendix alone — which is why the table below says *base and appendix* and
the appendix's first line links to its base.

Four more have **no base**, and say so in their first line: `architecture`, `protocol`,
`persistence`, `concurrency`. The first replaces a base that did not survive contact with this
project; the other three cover what ARC is and the package has nothing on.

Each fact has one home; a rule that needs a fact from elsewhere links to it rather than restating
it. The long reasoning is not in the rules at all — it is in [docs/adr/](docs/adr/), read on
demand.

### None of them is in front of you yet

A rule with `paths:` is loaded **only when a tool opens a file that matches it**, and only the
tools that open a file by path do that. Reading with `cat`, `sed` or `grep` shows you the text and
loads nothing. This page is the only thing here that is always in context, which is why the table
below is on it and not somewhere better organised.

Measured on 6 September 2026 over the ten sessions this repository has had: **seven of them loaded
not one rule**, and the two that loaded any are the two that opened files with the read tool rather
than through the shell. Nothing said so at the time, and nothing will. A turn that obeys no rule
looks exactly like a turn that had none to break — the same problem
[testing.project.md](.claude/rules/testing.project.md#the-gate-that-runs-every-turn-is-not-in-this-repository)
names about the gate, in the other direction.

So the table is a precondition, not an index: **open the rule before deciding, not after.** A rule
that arrives mid-change can only be checked against what is already written, which is the expensive
half of the work and the half that gets skipped. The measurement and what it does not authorise are
in [P021](docs/adr/P021-a-rule-that-never-arrived.md).

| Before you touch | Open first |
|---|---|
| any `.cs` at all | `coding-conventions`, base and appendix |
| `src/**` or `tests/**` | …and [architecture.project.md](.claude/rules/architecture.project.md) |
| `MessageStore.cs` | …and [persistence.project.md](.claude/rules/persistence.project.md) |
| `WaiterRegistry.cs`, `EventStream.cs`, `HubApp.cs` | …and [concurrency.project.md](.claude/rules/concurrency.project.md) |
| `src/Arc.Hub/**`, `Models.cs`, `ChannelService.cs` | …and `api-guidelines`, base and appendix |
| `src/Arc.Hub/**`, `src/Arc.Cli/**`, `Models.cs`, `docs/PROTOCOL.md` | …and [protocol.project.md](.claude/rules/protocol.project.md) |
| `tests/**`, `scripts/**`, `.github/workflows/**` | `testing`, base and appendix |
| `*.csproj`, `*.props`, `*.slnx`, `global.json`, `Dockerfile`, `.github/workflows/**` | `build-and-packages`, base and appendix |
| `docs/adr/`, `docs/specs/`, `docs/todo.md`, `docs/backlog.md` | no area rule governs these — they are the `close-increment` and `reconcile-rules` skills' subject |
| a rule in `.claude/rules/` | `reconcile-rules`, and the area's base if it has one |

**Half of what is versioned here matches no `paths:` at all** — 77 files of 122, and they are the
markdown this project largely consists of. For those the last two rows are the whole answer, and
this page is the only thing that reaches them.

---

## Working here

* Starting a change is the `feature-workflow` skill; finishing one is `close-increment`; after
  deciding something the rules did not anticipate, `reconcile-rules`. They live in the
  `dotnet-house` plugin, with both hooks and both subagents — **not here**. `ef-migration` is also
  in that plugin and has **no subject in this repository**: there is no ORM.
* **Open a file with the tool that opens files.** Reading or editing it through the shell works and
  costs you every rule that governs it, silently — the table above is what you are giving up. The
  shell is for what it is for: git, the build, the suites, searching across the tree.
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
[testing.project.md](.claude/rules/testing.project.md#the-gate-that-runs-every-turn-is-not-in-this-repository).
CI runs the same checks on `ubuntu-latest` for every push and pull request
([.github/workflows/gate.yml](.github/workflows/gate.yml)), which answers a different question:
whether any of it was ever Windows without saying so.

Current work: [docs/todo.md](docs/todo.md). Finished increments: [docs/specs/](docs/specs/).
