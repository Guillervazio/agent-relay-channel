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
[testing.project.md](testing.project.md#nothing-in-this-repository-runs-the-gate).

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

The base requires an injected `TimeProvider`. ARC does not have one yet: `DateTimeOffset.UtcNow`
is read directly in `ChannelService`, `MessageStore` and `Arc.Hub/Program.cs`. That is a **gap,
not a deviation** — it is work owed, listed in `docs/backlog.md`, and it is why no test here can
assert on a timestamp without a tolerance.

## Deviations

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
