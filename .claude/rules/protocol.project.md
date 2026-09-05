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

### When tightening would be breaking, the document is the half that moves

A contract that promises more than the code enforces has two repairs, and only one of them is
cheap. Tightening the code to match the document is a new refusal, which the test above almost
always rejects: the client that breaks is one that read an example rather than a rule nobody was
enforcing, and that client is not abusive. **Widening the document to state what the code has
always done is not breaking at all**, and it is the default repair.

Increment 08 took it twice. `refs` was documented as a JSON *object* and nothing checked it, so
the document now says any JSON value with the object as convention
([P017](../../docs/adr/P017-refs-is-any-json-value.md)). `refs` also stayed uncapped while `body`
is held to 256 KB, and a cap is a new refusal by the same test, so what changed is that the
request-wide limit is now written down instead of discovered.

**What this does not authorise: treating the document as the thing that yields whenever the code
disagrees with it.** The question is which of the two an honest client could have relied on. Here
the code was the wider of the two and had been for every released version, so no client could
depend on the refusal that never happened. Where the document is the *narrower* half and clients
have been obeying it, widening the code is adding a capability, and where the code is wrong about
something a client can already observe — a status code, a field's presence — the code is what
moves, `/v2` or not.

### Narrowing when a code is emitted is not changing what it means

`self_addressed` used to answer any request to oneself and now answers only a request to oneself
that asks to *wait* ([P018](../../docs/adr/P018-an-agent-may-queue-work-for-itself.md)). The code
is unchanged, its 422 is unchanged, and what it says when it arrives is unchanged — what changed
is that fewer calls are refused. Accepting what used to be refused is the safe direction, and it
is the same asymmetry as widening `AgentNamePattern`.

The direction is the whole of the rule. Emitting an existing code **in a case it did not cover
before** is the breaking half: a client branching on it now takes that branch somewhere it never
did, which is indistinguishable from the code having changed meaning.

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

**A difference that comes from how a surface carries a field is not such a bug.** `refs` arrives
as a separate string argument in MCP, inside the body in REST, and is parsed before anything is
sent in the CLI — so malformed `refs` are `invalid_refs` 422, `invalid_json` 400 and exit code 2
respectively, and all three are right. What is a bug is a difference that comes from what the
*channel* decides, because that lives in `ChannelService` and reaches all three.

The price of the exception is disclosure: **a published error code reachable on only one surface
says so in `PROTOCOL.md`, and says why.** `invalid_refs` does
([P017](../../docs/adr/P017-refs-is-any-json-value.md)). A code that is unreachable everywhere,
or reachable in one place and unexplained, is the defect — increment 08 found it twice.

One thing is genuinely MCP's alone: the `initialize` handshake, and the `instructions` it carries
([P014](../../docs/adr/P014-the-channel-explains-itself-in-the-handshake.md)). REST has no
handshake and the CLI has no session, so **nothing an agent must obey may live only there**. That
text tells a model how to use the channel well; a rule the channel enforces belongs in
`ChannelService`, where all three surfaces meet it, and the fact that two surfaces never see the
handshake is why `docs/AGENTS.md` still exists.
