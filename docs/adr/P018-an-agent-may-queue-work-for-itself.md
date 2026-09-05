# P018 — An agent may queue work for itself, and may not wait on it

## Context

`AskAsync` refused a request whose recipient was its sender with `self_addressed` 422. `NoteAsync`
had no such check, so a note to oneself had always been allowed. Two sibling operations disagreed
with no comment, rule or record saying why — which meant one of them was wrong and nothing said
which.

The channel's whole premise is that a CLI agent exists only during its turn
([P001](P001-long-polling-not-a-broker.md)). That is precisely the caller with a reason to leave
itself something for the next one.

## Decision

**An agent may address a request to itself.** It lands in its own mailbox like any other, `wait = 0`
answers `queued`, and it is there on the next turn.

**An agent may not wait on it.** `wait > 0` where sender and recipient are the same answers
`self_addressed` 422. The only party that could answer is the one blocked waiting, so the wait can
end only in a timeout — refusing says so at once instead of spending the caller's turn discovering
it.

**The refusal lives in both doors a wait can be asked for**: `AskAsync` and `AwaitResponseAsync`.

**Collecting an answer that already exists is never refused**, including one the caller gave itself
in an earlier turn, which `RespondAsync` has always allowed. In `AwaitResponseAsync` the check sits
*after* the lookup for a response, for that reason.

## Consequences

Forbidding the note was the other way to settle it, and
[protocol.project.md](../../.claude/rules/protocol.project.md) had already ruled it out before this
increment: an agent noting to itself is doing something undecided, not something abusive, so the
refusal would be breaking and would wait for a `/v2`.

What made this more than picking the cheaper half is that the two halves of the old refusal were
never the same thing. *Addressing* yourself is what a note already did and nothing was wrong with
it. *Waiting* on yourself is structurally impossible, and it is the only part the old check was
right about. Splitting them is why `self_addressed` narrows rather than disappears: a code
published with nothing left to emit it is the defect this increment closed twice elsewhere, and
creating a third would have been a poor trade.

Putting the check after `ValidateWait` is what lets it read the wait at all. It also means an
`ask` to oneself with an empty body now answers `empty_body` where it used to answer
`self_addressed`. Nothing documents or asserts which of two broken fields wins, and a rule about
the wait cannot fire before the wait is known to be valid.

The refusal is only useful if its recipient can read it, and over MCP no refusal was readable at
all until [P019](P019-a-refusal-a-model-cannot-read-is-not-a-refusal.md) — which is why the two
decisions are in the same increment.

## What this does not authorise

**Any general licence to relax a refusal because it is inconvenient.** What carried this is that
the refused thing was already allowed on a sibling operation, so the channel was not deciding
anything — it was disagreeing with itself.

**A second waiter mechanism.** An agent waiting on itself is a deadlock with one participant, and
it is refused rather than mitigated because nothing could resolve it. The two-agent case stays
what [PROTOCOL.md](../PROTOCOL.md#deadlock) says: a deadline, and `waiters` on `/healthz`.

**Reading `self_addressed` as being about identity.** It is about the wait. A caller that branches
on it should retry with `wait = 0`, not with a different recipient.
