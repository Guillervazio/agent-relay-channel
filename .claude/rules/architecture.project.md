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
Arc.Tests → Arc.Core, Arc.Cli
```

`Arc.Core` never references `Arc.Hub` or `Arc.Cli`, and neither surface references the other.

`Arc.Tests → Arc.Cli` is the one edge that is not a surface pointing at the core, and it exists for
exactly one reason: `ExitCodes` is published contract and a test freezes its numbers
([P009](../../docs/adr/P009-the-cli-exit-codes-are-contract.md)). The class stays `internal`,
opened with `InternalsVisibleTo`. What this does not authorise: testing the CLI's behaviour through
that reference. Its `Program.cs` is top-level statements with no seam, and reaching for one would
be a design change, not a test.

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

`Arc.Hub` calls `store.GetAsync`, `store.GetThreadAsync`, `store.ListAgentsAsync`,
`store.GetRecentAsync` and `store.ListThreadsAsync` directly, and `ArcTools.ThreadAsync` reaches
through `channel.Store`. These are **read-only projections for the observer**: they take no
decision, enforce no rule, and change nothing.

That is the whole licence. A read that has to decide who may see it is not a projection — it is a
channel operation, and it belongs behind `ChannelService`. Two of those direct reads are already
on the wrong side of that line: `GET /v1/messages/{id}` and `GET /v1/threads/{id}` are served
with **no authorisation at all**, so any authenticated agent reads any message body in the
channel. That is recorded in `docs/backlog.md` as a finding, and it is the reason this paragraph
names the boundary instead of leaving "projection" to taste.

## A channel operation ships on all three surfaces

A new operation appears in REST, in MCP and in the CLI, or this appendix says why not. A surface
that quietly lacks an operation is how the three stop being the same channel.

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
| An HTTP endpoint | `Arc.Hub/Program.cs` |
| An MCP tool | `Arc.Hub/ArcTools.cs` |
| A CLI subcommand | `Arc.Cli/Program.cs` |

The folder tree is not written down here. `Arc.slnx` and a directory listing describe it better
and do not go stale.
