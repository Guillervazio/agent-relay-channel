---
paths:
  - "**/*.cs"
---

# Coding conventions — this project

Appendix to [shared/coding-conventions.md](shared/coding-conventions.md). Everything there
applies; what follows is what is only true here.

## What the toolchain enforces here, measured

The base says the compiler fails on the style rules. On 4 September 2026, with the
`.editorconfig` and `Directory.Build.props` this repository has, that was measured rather than
assumed: `dotnet build -p:EnforceCodeStyleInBuild=true` over the whole solution reports **zero**
diagnostics, and it reported 311 before the three commits that closed them — 216 IDE0008, 90
IDE0011, 5 IDE1006.

Nothing else fires. No IDE0040, IDE0044, IDE0161, and no CA rule at the default `AnalysisMode`,
which is left where the SDK puts it. That is narrower than the base's list implies, and it is why
raising `AnalysisLevel` is a backlog entry with a trigger rather than a thing already done.

The `dotnet format` half of the base clause is the fragile half here too, and for the same reason:
`using` order, whitespace and CRLF are enforced only because the Stop hook runs `dotnet format`
every turn, and that hook arrives as a plugin. See
[testing.project.md](testing.project.md#the-gate-that-runs-every-turn-is-not-in-this-repository).

## Where deferred work is recorded

`docs/backlog.md`. There is no issue tracker, which is why the base rule says "outside the code"
rather than "open an issue" — a rule nobody can carry out teaches that rules are optional.

## Identifiers are strings, not strongly typed

The base asks for strongly typed identifiers. ARC's are `string`: `req_…`, `res_…`, `not_…`,
`thr_…`, and an agent name. This is a **deviation**, recorded below, and the reason it is not
drift is that every one of these crosses the wire as a string in three surfaces and is documented
that way in `docs/PROTOCOL.md`.

## The static classes this repository has

`ExitCodes` in `Arc.Cli` is the base's "compile-time constant table" case exactly: five `const int`
that a caller branches on, with nothing an instance would hold. `ArcJson` and `Help` are the same
shape.

`ChannelService` is the contrast. It looks like a candidate — its methods are close to pure — but
it holds a store, a registry and an event stream, so it is a class and it is injected.

## Time

The base's injected `TimeProvider` is in force for every instant the channel **writes**:
`ChannelService`, `MessageStore`, `EventStream` and the hub take it as a constructor parameter
defaulting to `TimeProvider.System`, and the hub registers the one singleton all four share.
`DateTimeOffset.UtcNow` must not reappear in any of them.

The default argument is what let the injection land without editing a single call site, and it is
**not** a licence to leave a new dependency optional. It is here because the alternative was a
required parameter in forty-four test constructions, which would have destroyed the evidence that
the refactor moved no behaviour. A dependency with real alternatives is required.

`ChannelEvent.At` is `required` for the same reason, and it is the shape to copy: a property
defaulted to `DateTimeOffset.UtcNow` is a second clock inside the type, unreachable from the
injected one, which makes the injection true of the code and false of the behaviour.

Two readers of the real clock survive on purpose, both in `docs/backlog.md` with what makes them
due: `WaiterRegistry`, whose waits are `Task.Delay` and so need the wait mechanism changed rather
than a timestamp, and `Arc.Cli`, which has no composition root to inject anything into.

## Deviations

### A read that can be refused throws; it does not return `null`

Replaces the base clause under *Missing versus refused*: "A query handler returns `null` for a
resource that does not exist and the transport turns that into a 404. … **Do not use an exception
for the query case.**"

`ChannelService.MessageAsync` and `ChannelService.ThreadAsync` are reads, and both throw
`ChannelException(not_found, …, 404)`. `GET /v1/messages/{id}` used to be the one place in this
repository where the base clause literally applied — `store.GetAsync` returned `null` and the
handler turned it into a 404 — and increment 07 reversed it on purpose.

The clause assumes one transport. There are three here, and a `null` says only "nothing for you":
each surface would then decide separately that nothing means 404, and that "not yours" and "not
there" must be worded identically down to the detail string. That last part is
[P016](../../docs/adr/P016-a-message-is-read-by-its-two-ends.md)'s actual content, and three
copies of it is the divergence [architecture.project.md](architecture.project.md) exists to
prevent. It is also the shape
[H011](../../docs/adr/house/H011-404-not-403-when-authorisation-filters-rows.md) already
prescribes in its own decision line: *one exception raises both*.

What this does not authorise: exceptions for reads in general. The observer's projections —
`ListAgentsAsync`, `GetRecentAsync`, `ListThreadsAsync` — return their data or an empty list and
throw nothing, because they refuse nobody. **The test is whether the read can be refused at all.**
Where it cannot, `null` or an empty list is still the answer, and an exception is control flow
wearing an error's clothes.

### Strongly typed identifiers

Replaces the base clause under *Domain models*: "Identifiers: strongly typed, never a bare
primitive."

ARC's identifiers are `string`. The clause is written for a domain model whose identifiers exist
to be confused with one another — a `ProductId` passed where a `SupplierId` was meant. ARC's are
opaque wire values carrying their own type in a three-character prefix, produced in one place,
never arithmetic, and never compared across kinds. A wrapper would be re-serialised at every one
of the three surfaces to arrive as the same string.

What this does not authorise: primitives for anything that has rules. An agent name is validated
by `AgentNamePattern` in one place and that stays the single door.
