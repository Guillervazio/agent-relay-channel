# P020 — A recovery window, not a state

## Context

`InboxAsync` marks the messages it is about to return as `delivered`, and then returns them. A
response lost in transit therefore takes those messages out of the default mailbox without the
client ever seeing them.

For a `request` that is survivable: `?unanswered=true` returns requests that were delivered and
are still unanswered, which is how an agent that died before replying picks the question back up.

For a `note` it is not. The recovery query ended in `AND kind = 'request'`, so notices were
excluded by construction, and there was no other way back to one. The channel had a message type
whose loss was permanent and undetectable.

Deleting that clause was the obvious repair and is the wrong one. **A request drains itself and a
note does not.** Answering moves a request to `answered`, which takes it out of the recovery set;
a note has no terminal state and stays `delivered` for ever. A recovery bounded only by status
would return every notice an agent had ever received, on every poll, growing without limit — a
regression in ordinary use dressed as a fix.

## Decision

**A new parameter, `?replay=N`, not a widened `?unanswered=`.** `unanswered` keeps its name, its
meaning and its query. Renaming it is breaking, but the reason that matters is that the two sets
have different shapes: one needs no bound because responding drains it, the other needs one
because nothing does. A single flag would have carried two bounding rules selected by `kind`,
which is the clause being removed, turned inside out.

**The bound is a window in seconds over `created_at`**: everything addressed to the caller inside
the last `N` seconds, whatever its kind and whatever its status, alongside whatever is `pending`
as usual. The three criteria are one `OR` inside one `WHERE`, so a message meeting two comes back
once.

**Counted back from the hub's own clock.** A caller says how long ago and never since when. That
is the only one of the two it still knows: the response it lost is precisely what would have
carried the ids and the timestamps, and a clock the hub never reads cannot be skewed against it.

**Refused rather than clamped.** `N` outside `0..86400` answers `invalid_replay` 422. A silently
narrowed window returns fewer messages and comes back indistinguishable from an honest empty
answer. `replay=0` is "do not look back" and is accepted, the way `wait=0` is "do not wait".

**Its own error code.** A client that passes both bounds must be able to tell which was refused;
`invalid_wait` could not say.

**Re-reading writes nothing.** No marking, no status change, no waiter woken. The same window
returns the same messages as many times as it is asked.

**The cap is a constant, not a hub setting.** `MaxWaitSeconds` is configurable because it decides
how long a connection is held, which is a property of the deployment. Nothing about how the hub
is started changes what a reasonable recovery window is.

## Consequences

The window is over `created_at` because there is no `delivered_at` column, and adding one is not
available: [P007](P007-the-schema-is-created-at-startup.md) creates the schema with
`CREATE … IF NOT EXISTS`, which does nothing to a table that already exists. So `replay` answers
"what was addressed to me recently", not "what you handed me recently". For the case it exists to
serve those coincide, because a message is delivered within seconds of being created.

It also makes `MessageStore.Format` load-bearing for comparison and not only for the round trip —
the `>=` is text against text, correct only because every row is fixed-width UTC. That is now
stated in [persistence.project.md](../../.claude/rules/persistence.project.md#timestamps).

The clause it falsified was in a file this change never edited:
[api-guidelines.project.md](../../.claude/rules/api-guidelines.project.md) said the inbox needed
no caller-supplied bound because it is one agent's mailbox. One agent's mailbox is not a bound
once part of it stops draining.

Nothing here is acknowledgement or retry. The hub re-attempts nothing, keeps no per-message
delivery state and is told nothing by the client, so [P001](P001-long-polling-not-a-broker.md) is
untouched. The data was never lost from the database either — `/v1/observe/history` had been
serving these notices all along ([P010](P010-the-observer-page-is-unauthenticated.md)), which is
what shows this was a gap in a query surface rather than in delivery.

## What this does not authorise

**Marking, counting or expiring anything on re-read.** The moment the hub records that a message
was replayed, this stops being a query and becomes a delivery mechanism with per-message state —
which is an acknowledgement with retries, and that is a change to P001 rather than an extension of
this record.

**Paging the inbox.** `replay` decides *which* messages are in the answer, not how much of a fixed
answer the caller receives. A `limit`, an `offset` or a cursor is a different thing and is still
refused.

**Fixing the marking itself.** The message is still marked delivered before the client has it, and
returned objects still report `status: "pending"` on that first read although the row is already
`delivered`. This record adds a way back; it does not claim the underlying order is right. So is
the duplicate delivery two simultaneous polls by the same agent receive — both remain in
[backlog.md](../backlog.md), and both are changes to when a message is marked and how a poll is
woken, which is why they were kept out of this one.

**Reading the window as a retention policy.** 86400 is a cap on how far a caller may ask back, not
a promise that anything is kept for a day or discarded after one. Nothing expires messages —
`MessageStatus.Expired` is still published and never produced.
