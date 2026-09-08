# P025 — What earns a place in the handshake

## Context

[P014](P014-the-channel-explains-itself-in-the-handshake.md) settled that the channel explains
itself in `initialize`, from one constant, so that no repository adopting it writes anything. Its
*what this does not authorise* names two things that may **not** go there: a rule the channel
actually enforces, and anything a tool's `[Description]` already says.

It never said what **earns** a place, because until now nothing had asked to be added. The text has
not grown since increment 05.

The candidate arrived from outside. A session in another repository ran two agents over this
channel and reported a failure it had hit: the agent answered richly in the channel — figures,
caveats, a verdict — and wrote a thinner report to disk. **The channel answer looked complete, so
the gap was invisible until somebody opened the file.** That report put the rule in a per-project
`AGENTS.md`.

That placement is the copy P014 exists to prevent, and it is wrong for a reason more specific than
"copies drift": the failure is not a property of that project. It is available to anybody who
follows the instruction this channel already gives — send references rather than content.

## Decision

The clause goes in `ArcInstructions`, and in [AGENTS.md](../AGENTS.md) for the two cases the
handshake does not reach. That is the one second copy P014 already sanctions, not a new one.

**And the test for the next candidate, which is what this record is actually for.** Four questions,
all of which must pass:

1. Is it about **using the channel**, or about doing the work well? Only the first.
2. Could `ChannelService` enforce it? If it could, it belongs there instead — that is P014's own
   prohibition, and prose a model may ignore is the weaker of the two homes.
3. Does it name a project, a machine or an agent? A test already fails on that.
4. Does a tool's `[Description]` already say it?

This clause passes the first with something stronger than a judgement: it is the **complement of a
clause already in the text**. The instructions say to send references rather than content because
the other side has its own clone. If the content is not in the file the reference names, that
clause is precisely what produces the failure. It closes a topic rather than opening one, which is
the shape to look for in the next candidate.

It passes the second because the channel cannot see the file. There is no version of this the hub
could check, so it is prose or it is nothing.

## Consequences

`PROTOCOL.md` does not move. It guarantees that the field arrives and is not empty, and that is the
whole of what a client may rely on; rewriting the text is explicitly **not breaking** —
[protocol.project.md](../../.claude/rules/protocol.project.md).

`HubEndpointTests` gains a fourth substring next to the three already there. That assertion is
brittle by nature, and it keys on wording that the protocol says no client may key on. What it
guards is that the **topic** is present, not that the sentence is unchanged, which is the same
thing the other three do and the reason it is worth having anyway: the text is a constant nobody
compiles against, so nothing else would notice it being deleted.

## What this does not authorise

**General good advice.** "Re-read the request before calling it done" came from the same report and
is deliberately not here: it is about doing the work, not about using the channel, so it fails the
first question. Somewhere it is worth saying — this is not that place.

**Growing the text because a failure happened.** A failure is evidence that something is missing.
It is not an argument about where the missing thing goes, and the four questions are what decides
that.

**Treating an outside document's conclusions as this project's.** Increment 13 was spent undoing
exactly that, and the distinction is the whole point of naming it twice: an outside **observation**
is evidence and is used here; an outside **claim about this project's scope** is not binding and
was wrong. The report that supplied this clause also asserted that supplying turns was outside this
project by design. One of those was worth taking and the other was not.
