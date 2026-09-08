---
paths:
  - "src/**/*.cs"
  - "tests/**/*.cs"
---

# Architecture — this project

Appendix to [shared/architecture.md](shared/architecture.md), **and it was not one until
increment 11**. The base used to describe a five-role Clean Architecture with aggregate-root
repositories, vertical slices and command handlers, which ARC would have had to deviate from five
times over; this file was written standalone and the finding was reported to the package. The
package demoted it — the shape is now
[shapes/clean-architecture.md](https://github.com/Guillervazio/dotnet-house/blob/master/shapes/clean-architecture.md),
which nothing inherits — and rewrote the base as the four clauses ARC and the originating project
had reached separately. This appendix stops repeating those four and answers them instead;
[P022](../../docs/adr/P022-the-base-that-came-back.md) is why adopting it beat staying standalone.

**Deviations: none.** Not a claim of virtue — the base is now short enough, and derived from this
repository among others, that there is nothing here to depart from. If that stops being true, the
section goes at the bottom like every other area's.

## Three roles

```
Arc.Cli  → Arc.Core
Arc.Hub  → Arc.Core
Arc.Core → the BCL and Microsoft.Data.Sqlite, and nothing else
Arc.Tests → Arc.Core, Arc.Cli, Arc.Hub
```

Forbidden outright, as edges rather than as an absence: `Arc.Core → Arc.Hub`, `Arc.Core → Arc.Cli`,
`Arc.Hub → Arc.Cli`, `Arc.Cli → Arc.Hub`.

`Arc.Tests → Arc.Cli` and `Arc.Tests → Arc.Hub` are the two edges that are not a surface pointing
at the core. Both exist so the suite can reach a surface's composition root, and both surfaces have
one because increment 04 built them:

* **`HubApp.BuildAsync(HubOptions)`** returns the assembled `WebApplication`. Its
  `configureWebHost` parameter is where a test swaps the server for an in-memory one.
* **`CliRunner`** takes its output, error and input streams by constructor and an optional
  `HttpMessageHandler` by parameter.

`Program.cs` on each side keeps only what has no seam and needs none: read the environment, refuse
it if it will not work, run. **Neither is a place to put a decision.** A rule that lands in
`Program.cs` is a rule no test can reach, which is the state this increment ended.

`ExitCodes` and `ArcTools.AgentKey` stay `internal`, opened with `InternalsVisibleTo` — the exit
codes because they are published contract a test freezes
([P009](../../docs/adr/P009-the-cli-exit-codes-are-contract.md)), the key so the test uses the
middleware's own constant rather than a second copy of the literal.

`Arc.Core` opens the same door for `MessageStore.OpenAsync`, and the bar it had to clear is the one
to apply next time: the connection's pragmas are a decision this repository records, no public
method reveals them, and a defect in them had already survived a full increment unseen. Widening
visibility to observe something the design deliberately hides is the case; widening it to avoid
arranging a test is not.

There is no dependency-injection container in `Arc.Core`: the hub wires it up, the CLI does not
need one.

## Supplying a turn is not a role here

The channel makes a turn **wait**; nothing in this solution makes a turn **happen**. What opens an
agent's turn lives on that agent's own machine, as configuration, and is not a project in the graph
above — [P024](../../docs/adr/P024-who-supplies-the-turn.md) carries the argument.

The reason is the topology rather than a preference: the deployment is two PCs, and a hub on one of
them cannot start a process on the other. It could not have been `Arc.Hub` even if that had been
the tidier place to put it.

Forbidden, and each of these is what somebody reaches for first:

* **A hub feature** — an endpoint or a background service that runs a command when a message
  arrives. It cannot reach the other machine, and it turns one shared token into arbitrary
  execution on every machine on the channel.
* **A CLI subcommand.** `arc turn` is not binding an input and calling exactly one `ChannelService`
  method, which is the whole of what a surface does. The CLI is what an agent runs *inside* a turn.
* **A provider's invocation committed anywhere in this tree.** The executable, its flags and its
  sandbox settings are what a version changes underneath you, and
  [build-and-packages.project.md](build-and-packages.project.md) has no row for somebody else's
  CLI. [demo/README.md](../../demo/README.md) carries a placeholder for that reason, and
  `ARC_TURN_<agent>` is read by **no** binary in this repository — an agent's own instructions read
  it, which is why it belongs in no configuration table and why grepping `src/` for it finds
  nothing.

What this does not authorise: calling anything inconvenient "agent-side". The test is whether it
must run on a machine the hub cannot reach. Everything the channel *decides* still lives in
`ChannelService`, where all three surfaces meet it.

## Which role translates, and which one decides

The base's edge clause, answered: **the rules of the channel live once, in `ChannelService`.**
`Arc.Hub`, `Arc.Cli` and any future surface *translate and nothing else* — bind the input, call
exactly one `ChannelService` method, render the result in the idiom of that surface.

Forbidden in a surface:

* a decision `ChannelService` could have made,
* a second copy of a validation,
* reaching into `MessageStore` for something a `ChannelService` method already exposes.

There are **three** edges here, which is what makes the base's reason concrete rather than
theoretical: no one person uses all three, so two of them answering the same question differently
is invisible until somebody depends on the one that is wrong.

### The exception, named because it is real

`Arc.Hub` calls `store.ListAgentsAsync`, `store.GetRecentAsync` and `store.ListThreadsAsync`
directly. These are **read-only projections for the observer**: they take no decision, enforce no
rule, and change nothing, and they answer the same thing to every caller because the panel reads
the whole channel by design.

That is the whole licence, and it is now the whole list — with one addition increment 08 made
permanent rather than removed. `ArcTools.ParseRefs` throws `invalid_refs` from the MCP surface,
and it stays there because it is **binding, not a rule**: MCP is the only surface where `refs`
arrives as a string of its own, so parsing it is the same work `HubApp` does when it turns a
request body into a record. `ChannelService` never sees a string to reject.

The boundary is that nothing else may join it. A check that could be written against the
`JsonElement` rather than against the text is a rule, belongs in `ChannelService`, and the fact
that one surface is more convenient is not an argument — see
[P017](../../docs/adr/P017-refs-is-any-json-value.md), which declines to add exactly such a check
for a different reason and would have put it in `ChannelService` had it added one. **A read that has to decide who may see
it is not a projection — it is a channel operation**, and it belongs behind `ChannelService`.
`GET /v1/messages/{id}` and `GET /v1/threads/{id}` were on the wrong side of that line until
increment 07: they reached `store.GetAsync` and `store.GetThreadAsync` directly and authorised
nobody. They now go through `ChannelService.MessageAsync` and `ChannelService.ThreadAsync`, and
`ArcTools.ThreadAsync` no longer reaches through `channel.Store` —
[P016](../../docs/adr/P016-a-message-is-read-by-its-two-ends.md).

`ChannelService.Store` is still public and `ArcTools.AgentsAsync` still goes through it for
`ListAgentsAsync`. That is the same list above reached by another door, not a second licence: what
travels through `channel.Store` must be an entry on it, and `arc_agents` is one.

What this does not authorise: adding to that list. The test for a new direct read is whether it
would answer **the same thing to every caller**. The moment the answer depends on who is asking it
is an operation, and the reason is this section's own: put it in a surface, and the other two
surfaces will decide it differently or not at all.

## A channel operation ships on all three surfaces

A new operation appears in REST, in MCP and in the CLI, or this appendix says why not. A surface
that quietly lacks an operation is how the three stop being the same channel.

**`ChannelService.MessageAsync` is REST's alone, and here is why not.** `GET /v1/messages/{id}`
exists to make the `Location` of a 202 resolvable: a REST client whose `wait` ran out has to be
able to find the request it just created without rebuilding the URL. MCP and the CLI reach that
same thing through `arc_await` and `arc await`, which take the request id and return the answer,
so neither has ever had a reason to fetch a message by id. It became a channel operation in
increment 07 only because it now decides who may read it — the operation is not new, the
authorisation is.

What this does not authorise: leaving the next one absent. This entry exists because "the appendix
says why not" is satisfied by writing the reason down, and an operation missing from two surfaces
with no entry here is the failure the section names, not a smaller version of it.

## What "no new patterns" amounts to here

The base forbids them; this is the state it is protecting. `Arc.Core` has **no interfaces at all**,
which is H002 satisfied by absence rather than by argument, and there is no mediator, no
dispatcher, no repository interface and no reflection-based registration anywhere.

So the base's test lands on this repository with one answer already known: "so a test can
substitute it" fails it here in particular, because the real store runs on a temporary file in one
millisecond and substituting it removes no constraint at all.

## Where a type goes

| Kind of thing | Where |
|---|---|
| A record that crosses the wire | `Arc.Core/Models.cs` |
| A rule of the channel | `Arc.Core/ChannelService.cs` |
| SQL, and only SQL | `Arc.Core/MessageStore.cs` |
| Waiting, waking, cancelling | `Arc.Core/WaiterRegistry.cs`, `Arc.Core/EventStream.cs` |
| An HTTP endpoint | `Arc.Hub/HubApp.cs` |
| An MCP tool | `Arc.Hub/ArcTools.cs` |
| Prose the handshake sends a model | `Arc.Hub/ArcInstructions.cs` |
| A CLI subcommand | `Arc.Cli/CliRunner.cs` |

That last one is one constant and no behaviour, and it stays that way. It is not where a tool's
own documentation goes — that is its `[Description]` — and it is not where a rule of the channel
goes, because a rule that lands there is enforced on nobody: two of the three surfaces never see
the handshake at all.

What **does** earn a place there is four questions, in
[P025](../../docs/adr/P025-what-earns-a-place-in-the-handshake.md) and stated once in
[api-guidelines.project.md](api-guidelines.project.md#the-mcp-surface-adds-no-operation-rest-lacks).
Ask them before adding a sentence to that constant; it had not grown since increment 05, and the
reason to keep it short is the reason it works.
