---
paths:
  - "tests/**/*.cs"
---

# Testing — this project

Appendix to [shared/testing.md](shared/testing.md).

## Suite inventory

| Project | Scope | Isolates with a container? | In the fast gate? |
|---|---|---|---|
| `tests/Arc.Tests/Arc.Tests.csproj` | The channel's rules and its store against a real SQLite file, the waiter registry in memory, the hub's whole pipeline through `TestHost`, the seven MCP tools, the CLI through a fake transport, and the published error and exit codes | no | yes |

One row, one project referencing `Microsoft.NET.Test.Sdk`. Complete.

## The four categories, here

**Unit** is `WaiterRegistryTests`, `ArcErrorsTests`, `EventStreamTests` and `CliRunnerTests` —
the last through a fake `HttpMessageHandler`, so it touches no socket. **Persistence** is
`MessageStoreTests` and `ConnectionPragmaTests`, against the engine ARC ships rather than a
substitute. `ClockTests` spans both.

**Integration** is `ChannelServiceTests` — the rules of the channel against the real store and the
real waiter registry, nothing substituted — plus `ArcToolsTests`, which drives the seven MCP tools
over the same channel.

**Scenario** is `HubEndpointTests`: the hub's real pipeline in memory through `TestHost`, asserting
the response envelope and the status mapping the contract publishes, **including the 401 and the
422 refusals**. That row was empty until increment 04, and what stood in for it was
`scripts/test-all.sh` — which is still run, still drives all four surfaces against a real hub on
port 8791, and is still not xunit and not in the fast gate.

The division of labour between them is worth stating, because it decides where a new test goes.
`HubEndpointTests` owns anything the pipeline decides: status codes, headers, refusals.
`test-all.sh` owns what only a real process can show — the published `arc.exe` actually running,
Kestrel's own behaviour under a long poll, and the observer page's event stream over a real
connection. A check that could live in either belongs in xunit, because that is the one a gate
runs.

## Deviations

### The database is a file, not a container

Replaces the base clause under *The database is real*: "A real engine in a container … One
container per test project."

`MessageStoreTests` opens a temporary SQLite file under `Path.GetTempPath()`, deleted in
`Dispose` along with its `-wal` and `-shm`. [H008](../../docs/adr/house/H008-real-database-in-tests-never-inmemory.md)
exists to stop a test running against an engine that is not the deployed one, and here a container
would be exactly that: ARC embeds SQLite in-process, so the container would introduce a second
engine rather than remove a substitute. The isolation the base gets from resetting data, this
suite gets from a fresh file per test class.

What this does not authorise: `Microsoft.Data.Sqlite`'s in-memory mode. That is a different
engine configuration with different locking, and it is the thing H008 names.

### The tools are xunit's own

Replaces the base clause under *Tools*: "FluentAssertions for assertions. NSubstitute for
substitutes … `FakeTimeProvider` for time."

ARC uses `Assert` and no substitute library. `FakeTimeProvider` is the one part of the clause this
repository keeps rather than replaces. Each has its own reason and none of them is preference:

* **Assertions.** FluentAssertions 8 is not free for commercial use, and adding version 7 to reach
  a fluent syntax is a package approval bought with nothing. `Assert.Equal` says the same thing.
* **Substitutes.** There is nothing to substitute. `Arc.Core` has **zero** interfaces, which is
  [H002](../../docs/adr/house/H002-single-implementation-interfaces.md) satisfied by absence, and
  the store under test is the real one on a temporary file.
* **Time.** No longer a deviation at all. `Microsoft.Extensions.TimeProvider.Testing` is approved
  for this project, and `ClockTests` uses `FakeTimeProvider` to pin the exact instant each surface
  writes. The approval buys the double and nothing else: `TimeProvider` itself is in the BCL.

What this does not authorise: a tolerance around `DateTimeOffset.UtcNow` in a new test. An instant
the channel writes is now assertable exactly, and a test that compares against the real clock is
asserting that the injection did not happen. The one place still measuring real elapsed
milliseconds is `WaiterRegistryTests`, whose waits are `Task.Delay` — that is a change to the wait
mechanism, it is in `docs/backlog.md` with its trigger, and it is a precedent for nothing.

### Test names are in Spanish

Replaces the base clause under *Naming and shape*: `MethodName_Should_ExpectedBehaviour_When_Condition`.

The 49 existing tests read `Una_senal_previa_a_la_espera_no_se_pierde`. They say the same thing
the pattern asks for — subject, expected behaviour, condition — in the language the rest of this
repository's prose is written in. Renaming them buys a shape and loses a sentence.

What this does not authorise: a name that is not a sentence. `Test1` and `SignalTest` fail this
rule in Spanish as much as in English.

## Nothing in this repository runs the gate

The Stop hook that runs `dotnet format`, `dotnet build` and this suite every turn is **not here**.
It arrives from the `dotnet-house` plugin, declared in `.claude/settings.json`.

Three ways it stops running, none of which announces itself: the plugin fails to resolve, its
entry in `enabledPlugins` is turned off, or the hook's script is not found. That last one is worth
naming precisely, because it is how another repository came to believe it had a gate it did not
have: **`$CLAUDE_PROJECT_DIR` is not expanded in a hook's `command`**. It reaches the shell
verbatim, resolves to nothing, and the hook fails with a *non-blocking* status. The plugin's
`hooks.json` uses `${CLAUDE_PLUGIN_ROOT}`, which the harness does expand — that is why it works.

A green turn is therefore not evidence that anything ran, and "I saw no complaint" is not evidence
in either direction. What settles it is **seeing the gate block a turn**.

This repository deliberately has no `.claude/hooks/stop-gate.config.json`. The override exists —
it lives there and takes `solution`, `fastTestProjects`, `formatMode` and `blockOn` — and ARC does
not need it: discovery finds `Arc.slnx` because it is the only solution in the root, and
`tests/Arc.Tests` because it is the only project referencing `Microsoft.NET.Test.Sdk` without
`Testcontainers`. Writing one would spend a criterion the package is being measured against here.

What this does not authorise: keeping a local copy of the hook "as insurance". Two copies is how
you get one that is stale and one that is dead, with nothing saying which is which.

## What `scripts/test-all.sh` needs that this suite does not

`curl` and `python`. Not `python3` — the scripts call `python`, and on the machine this was
written on `python3` does not exist while `python` resolves to a conda environment that is on
`PATH` by accident. `jget()` swallows stderr, so a missing interpreter produces an empty string
and the test fails reporting a content mismatch. See `docs/backlog.md`.
