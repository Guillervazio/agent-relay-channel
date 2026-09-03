---
paths:
  - "docs/PROTOCOL.md"
  - "src/Arc.Core/Models.cs"
  - "src/Arc.Cli/**/*.cs"
  - "src/Arc.Hub/**/*.cs"
---

# The protocol — this project

**This area has no shared base.** ARC publishes a wire contract that two independently operated
agents depend on, and nothing in `dotnet-house` covers what makes such a contract break.

## `docs/PROTOCOL.md` is the contract

Not a description of it. A change to the wire that does not change that file **in the same
commit** is a defect, not an omission — the two copies drift silently, and the reader who is
wrong is the one who trusted the document.

## What is breaking

* Removing or renaming a field a client reads.
* Changing the status code an existing error answers with.
* Changing a header name: `X-ARC-Agent`, `X-ARC-Token`, `X-ARC-Provider`.
* Narrowing `AgentNamePattern`. Widening it is not breaking; narrowing invalidates names already
  in use.
* Changing a value of `MessageKind`, `MessageStatus`, or an `outcome`.
* Changing what a CLI exit code means.

A breaking change is a new `/v1` → `/v2` path prefix with new handlers. The old ones freeze.

## What is not breaking

* Adding an optional field. Clients ignore what they do not read.
* Widening an opaque identifier. Nothing parses an id, the column is `TEXT` with no length, and
  `PROTOCOL.md` shows ids only as examples. **Verify that before relying on it** —
  `grep -rn '\[\.\.\|Length ==\|Substring' --include=*.cs src` and `grep -rn 'req_' scripts/
  src/Arc.Hub/ui/` — because the claim is about every consumer, not only the ones in this
  repository.
* Adding an endpoint, an MCP tool, or a CLI subcommand.
* Adding a **new** error code. Changing the meaning of an existing one is breaking.

## Identifiers

A three-character prefix that says what the thing is — `req_`, `res_`, `not_`, `thr_` — followed
by an opaque, time-ordered suffix. The prefix is for a human reading a log; **nothing parses it**,
and code that switches on it has made a display detail load-bearing.

See [P005](../../docs/adr/P005-message-identifiers.md), and read its
*What this does not authorise* before changing how the suffix is generated: truncating a UUIDv7
below its random field is worse than the random identifier it would replace.

## The CLI's exit codes are published contract

`Arc.Cli.ExitCodes`: 0 ok, 1 error, 2 usage, 3 timeout, 4 empty. `docs/AGENTES.md` tells an agent
to branch on them, which makes them as much a contract as any JSON field.

A code never changes meaning. A new state takes a new number. And a test **spells the number as a
literal** rather than referencing the constant — which is
[H012](../../docs/adr/house/H012-an-error-code-is-defined-once-and-a-test-keeps-the-literal.md) applied to
an exit code: a test asserting `ExitCodes.Timeout == ExitCodes.Timeout` passes through exactly the
change it exists to catch.

There are no such tests yet. `docs/backlog.md`.

## Three surfaces, one wire

REST, MCP and the CLI describe the same operations. Where they differ it is in idiom only: REST
returns JSON, MCP returns prose a model reads, the CLI returns prose a person reads plus an exit
code. A difference in *behaviour* between two surfaces is a bug in whichever one departed from
`ChannelService` — see [architecture.project.md](architecture.project.md).
