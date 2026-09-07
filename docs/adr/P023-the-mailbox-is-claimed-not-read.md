# P023 — The mailbox is claimed, not read and then marked

## Context

`InboxAsync` read the mailbox with one call and marked what it had read with a second one. Between
the two fitted another poll by the same agent: both read the same pending rows, and **both HTTP
responses carried the message**, although only one `UPDATE` marked it — the other's `WHERE
status = 'pending'` matched nothing.

The database was coherent the whole time. That is why this survived nine increments: what broke was
the *answer*, not the row, and every assertion anybody had written looked at the row. Reaching it
needs two terminals waiting on the same mailbox, which is the usage
[docs/AGENTS.md](../AGENTS.md) describes.

The same call also reported `status: "pending"` for a row it had just set to `delivered`, so a
first read lied about the state it had itself produced.

## Decision

**One operation, `MessageStore.ClaimInboxAsync`, doing both inside one transaction**, replacing
`GetInboxAsync` and `MarkDeliveredAsync` at the mailbox path. It returns the messages **and the
ids it actually claimed**, which are not the same thing: a message brought back by `?replay=` was
already delivered and is returned without being claimed again.

**It is [H007](house/H007-optimistic-concurrency-where-an-update-derives-from-a-read.md), the shape
`AddResponseAsync` already uses.** The decision to hand a message to this caller came from a row
that was read, so the `UPDATE`'s `WHERE` repeats the condition rather than trusting the read. The
affected-row count is then what says whether this call is the one that took it.

**The returned status is the row's, not the read's.** A claimed message comes back `delivered`.
`docs/PROTOCOL.md` has always said `pending → delivered (when read from the mailbox)`, so the code
was the half that was wrong, and
[protocol.project.md](../../.claude/rules/protocol.project.md) says the code is what moves in that
case — no `/v2`.

**`GetInboxAsync` and `MarkDeliveredAsync` are deleted rather than kept.** Neither had a caller
left outside the test suite. A store method alive only because its own tests call it is what
[architecture.project.md](../../.claude/rules/architecture.project.md) refuses when it says
widening a seam to arrange a test is not the case that justifies one — and the tests lost nothing,
because a row becomes delivered in a test now exactly as it does in the hub.

## Consequences

Two polls of one mailbox now serialise on a write transaction rather than overlapping. At this
scale that is a lock held for the length of one indexed `SELECT` and a handful of single-row
`UPDATE`s, with `DefaultTimeout = 30` behind it — the same busy timeout every other write already
relies on. A poll that is *waiting* holds nothing: the wait happens between claims, never inside
one.

A claim that returns nothing still opens a transaction. That is the cost of the fix being in one
place instead of two, and it buys the property that no caller can be handed a message another
caller was also handed.

The observer's `delivered` event now carries the ids this call claimed, rather than every pending
id it read. Before, two overlapping polls could each announce the same delivery.

## What this does not authorise

**Reading this as acknowledgement.** The message is still marked before the client has it. A
response lost in transit still takes it out of the default mailbox, and `?replay=N`
([P020](P020-a-recovery-window-not-a-state.md)) is still the way back. The hub re-attempts nothing,
keeps no per-message delivery state and is told nothing by the client, so
[P001](P001-long-polling-not-a-broker.md) is untouched. Fixing *that* half means the client saying
it received something, which is a different record and a different channel.

**Putting the wait inside the transaction.** It would hold a write lock for up to `ARC_MAX_WAIT`
seconds and stop every other agent's mailbox, turning a long poll into an outage. The claim is
short by construction and the wait sits between two claims.

**Reading "one transaction" as a licence elsewhere.** The reason this one is a transaction is
H007's: a write whose correctness depends on a row that was read. A read that decides nothing does
not become safer by being wrapped in one, and a transaction around work that waits is the mistake
above.

**Believing the race is now tested.** `Sondeos_simultaneos_entregan_cada_mensaje_una_sola_vez`
exercises eight polls over five messages and asserts each is delivered exactly once. It cannot
guarantee the interleaving happened, and its own comment says so. What settles the defect is not
that test but that the gap it exploited no longer exists outside the store: there is no way, from
`ChannelService` or anywhere else, to read the mailbox without claiming it.
