---
paths:
  - "src/**/*.cs"
  - "tests/**/*.cs"
---

# Architecture — this project

**This area has no shared base, deliberately.** `dotnet-house`'s `shared/architecture.md`
describes a five-role Clean Architecture with aggregate-root repositories, vertical slices and
command handlers. ARC would have to deviate from five of its clauses, and by the package's own
criterion that many deviations means the base was a project decision in disguise. The finding is
in `docs/backlog.md` and belongs to the package, not to this repository.

## Three roles, not five

```
Arc.Cli  → Arc.Core
Arc.Hub  → Arc.Core
Arc.Core → the BCL and Microsoft.Data.Sqlite, and nothing else
Arc.Tests → Arc.Core, Arc.Cli, Arc.Hub
```

`Arc.Core` never references `Arc.Hub` or `Arc.Cli`, and neither surface references the other.

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

## The rule this file exists for

**The rules of the channel live once, in `ChannelService`.** `Arc.Hub`, `Arc.Cli` and any future
surface *translate and nothing else*: bind the input, call exactly one `ChannelService` method,
render the result in the idiom of that surface.

Forbidden in a surface:

* a decision `ChannelService` could have made,
* a second copy of a validation,
* reaching into `MessageStore` for something a `ChannelService` method already exposes.

The reason is not tidiness. There are three surfaces over one channel, and the failure mode is
that two of them answer the same question differently — which nobody notices, because no one
person uses all three.

### The exception, named because it is real

`Arc.Hub` calls `store.ListAgentsAsync`, `store.GetRecentAsync` and `store.ListThreadsAsync`
directly. These are **read-only projections for the observer**: they take no decision, enforce no
rule, and change nothing, and they answer the same thing to every caller because the panel reads
the whole channel by design.

That is the whole licence, and it is now the whole list. **A read that has to decide who may see
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

## No new architectural patterns

There is no mediator, no dispatcher, no repository interface, no reflection-based registration —
and no interfaces at all in `Arc.Core`. Adding the first one is a decision that needs
[H002](../../docs/adr/house/H002-single-implementation-interfaces.md)'s test: it must remove a
constraint that cannot be removed otherwise, and "so a test can substitute it" is not that
constraint while the real store runs on a temporary file in one millisecond.

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

The folder tree is not written down here. `Arc.slnx` and a directory listing describe it better
and do not go stale.
