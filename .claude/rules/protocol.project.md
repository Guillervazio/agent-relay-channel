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
* Ceasing to send `instructions` in the MCP `initialize` result, or sending it empty.
  `PROTOCOL.md` guarantees the field arrives and is not empty, and that guarantee is the whole
  of what a client may rely on.
* **Refusing what a published route used to answer.** Narrowing who or what a route serves
  invalidates clients already relying on it, the same way narrowing `AgentNamePattern`
  invalidates names already in use.

A breaking change is a new `/v1` → `/v2` path prefix with new handlers. The old ones freeze.

### The one exception to that last clause

A refusal is **not** breaking when no honest client could have depended on the answer it removes —
when the old behaviour was the defect and not the contract. Increment 07 narrowed
`GET /v1/messages/{id}` and `GET /v1/threads/{id}` to the two ends of a message without a `/v2`,
because the only caller that loses an answer is one reading a message it was not a party to, and
that caller is what was being fixed
([P016](../../docs/adr/P016-a-message-is-read-by-its-two-ends.md)).

**The test is not "is this a security fix".** It is whether an honest client could hit the new
refusal. Forbidding `note` to oneself would fail that test — an agent noting to itself is not
doing anything wrong, only something undecided — so that one waits for a `/v2` or stays as it is.
The exception is narrow on purpose: "the old behaviour was a defect" is available to argue about
almost any change, and what keeps it honest is naming the client that breaks and showing it could
only be an abusive one.

## What is not breaking

* Adding an optional field. Clients ignore what they do not read.
* Widening an opaque identifier. Nothing parses an id, the column is `TEXT` with no length, and
  `PROTOCOL.md` shows ids only as examples. **Verify that before relying on it** —
  `grep -rn '\[\.\.\|Length ==\|Substring' --include=*.cs src` and `grep -rn 'req_' scripts/
  src/Arc.Hub/ui/` — because the claim is about every consumer, not only the ones in this
  repository.
* Adding an endpoint, an MCP tool, or a CLI subcommand.
* Adding a **new** error code. Changing the meaning of an existing one is breaking.
* Rewriting the text of `instructions`. It is prose a model reads, not a field a client parses,
  and no client may key on its wording.

## Identifiers

A three-character prefix that says what the thing is — `req_`, `res_`, `not_`, `thr_` — followed
by an opaque suffix. The prefix is for a human reading a log; **nothing parses it**, and code that
switches on it has made a display detail load-bearing.

The suffix is **not ordered**: it is `Guid.NewGuid().ToString("n")[..16]`, 64 random bits. P005's
decision line calls it time-ordered and its own *What this does not authorise* section explains
why it is not, which is the contradiction to know about before quoting either half. Nothing here
may assume an id sorts chronologically — the queries order by `created_at`, and that is why.

See [P005](../../docs/adr/P005-message-identifiers.md), and read its
*What this does not authorise* before changing how the suffix is generated: truncating a UUIDv7
below its random field is worse than the random identifier it would replace.

## The CLI's exit codes are published contract

`Arc.Cli.ExitCodes`: 0 ok, 1 error, 2 usage, 3 timeout, 4 empty. `docs/AGENTS.md` tells an agent
to branch on them, which makes them as much a contract as any JSON field.

A code never changes meaning. A new state takes a new number. And a test **spells the number as a
literal** rather than referencing the constant — which is
[H012](../../docs/adr/house/H012-an-error-code-is-defined-once-and-a-test-keeps-the-literal.md) applied to
an exit code: a test asserting `ExitCodes.Timeout == ExitCodes.Timeout` passes through exactly the
change it exists to catch.

`ArcErrorsTests.Los_codigos_de_salida_del_cli_no_cambian` is that test, and it spells `0` to `4`.
`Arc.Tests` therefore references `Arc.Cli`, and `ExitCodes` is `internal` with an
`InternalsVisibleTo` — opened to the suite rather than made public, because nothing outside the
CLI uses them.

## Three surfaces, one wire

REST, MCP and the CLI describe the same operations. Where they differ it is in idiom only: REST
returns JSON, MCP returns prose a model reads, the CLI returns prose a person reads plus an exit
code. A difference in *behaviour* between two surfaces is a bug in whichever one departed from
`ChannelService` — see [architecture.project.md](architecture.project.md).

One thing is genuinely MCP's alone: the `initialize` handshake, and the `instructions` it carries
([P014](../../docs/adr/P014-the-channel-explains-itself-in-the-handshake.md)). REST has no
handshake and the CLI has no session, so **nothing an agent must obey may live only there**. That
text tells a model how to use the channel well; a rule the channel enforces belongs in
`ChannelService`, where all three surfaces meet it, and the fact that two surfaces never see the
handshake is why `docs/AGENTS.md` still exists.
