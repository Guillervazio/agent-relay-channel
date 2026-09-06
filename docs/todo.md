# Current work

**Increment 09 — a note you can read twice.** Branch `feature/a-note-you-can-read-twice`.

## What this increment is for

A `note` whose delivery response was lost in transit is unrecoverable. `InboxAsync` marks the
messages delivered and *then* returns them, so a response that never arrives has already emptied
the default mailbox, and the only recovery path the channel publishes —
[`?unanswered=true`](PROTOCOL.md) — is restricted to requests by
`AND kind = 'request'` in `MessageStore.GetInboxAsync`. The notice is gone from the agent's view
for good.

**Scope: the recovery query, and nothing else.** When a message is marked delivered does not move,
and neither does how `WaiterRegistry` wakes a poll. Duplicate delivery from two simultaneous polls
by the same agent is the *next* increment and goes alone, because that is where the risk is.

### Why this is not a filter to delete

A `request` drains itself: answering moves the row to `answered`, which takes it out of the
recovery set. **A `note` has no terminal state** — it stays `delivered` for ever. So a recovery
mode that includes notices and is not bounded by something other than status returns the entire
history on every poll and grows monotonically. That is not a recovery, it is a regression in
ordinary use, and it is why the shape of the fix is not "remove the `kind` clause".

### What the data already says

The notice is not lost from the database, only from the mailbox: `/v1/observe/history` still
serves it, body and all, to any holder of the token. This increment is a gap in a **query
surface**, not in a delivery mechanism — no acknowledgement, no retry, no per-message state, and
nothing the hub re-attempts. [P001](adr/P001-long-polling-not-a-broker.md) is untouched, and a
design that starts to need any of those four has left this increment.

## The three decisions, taken before any code

| Question | Decision | Why |
|---|---|---|
| Widen `?unanswered=true`, or a new mode? | **A new parameter, `replay=N`.** `unanswered` keeps its name, its meaning and its query, and the two compose | Not only that the name would become a lie — a notice is never "unanswered" — but that the two sets have different shapes. `unanswered` needs no bound because responding drains it; notices need one because nothing drains them. A single flag would carry two bounding rules selected by `kind`, which is the `AND kind = 'request'` we are removing, turned inside out. Renaming is breaking; adding is not |
| What does it return? | **A window in seconds over `created_at`**: everything addressed to the caller inside the last `N` seconds, plus the `pending` ones as always, in chronological order. `N` above the cap is **refused with `422 invalid_replay`**, never clamped | Bounded by construction, and no clock skew: the hub does the arithmetic against the `TimeProvider` it already has, and the caller only says *how long ago*. That is the one thing a caller whose response was lost still knows — it never learned the ids or the timestamps, but it knows when it polled. Refusing rather than clamping is [`ValidateWait`](../src/Arc.Core/ChannelService.cs)'s reasoning verbatim: a silently narrowed window returns less and the caller cannot tell. The window is over `created_at` because **there is no `delivered_at` column**, and adding one is not available — [P007](adr/P007-the-schema-is-created-at-startup.md), `CREATE … IF NOT EXISTS` does nothing to a table that already exists |
| Does re-reading mark anything? | **Pure. It writes nothing** — no marking, no status change, no signal. The `pending` rows in the same response are still marked exactly as today | Marking before the client has it is the defect; repeating it on the recovery path would make the second read differ from the first and the recovery unrepeatable. A re-read that writes is also the first step towards an acknowledgement with retries, which is the line [P001](adr/P001-long-polling-not-a-broker.md) draws |

## Phases

Every phase is closed in the same commit that updates its row.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | The mailbox can be re-read within a window: `Arc.Core`, REST, and `PROTOCOL.md` in the same commit | done | this commit |
| 2 | MCP and the CLI carry it, and the handshake says it exists | pending | |
| 3 | The suites drive it against a running hub | pending | |
| 4 | Close: the record, the rules it made false, and what the backlog no longer owes | pending | |

Phase 1 is core and one surface together on purpose: the parameter added to `GetInboxAsync` with
no caller but a test is unused code, and unused code fails nothing — the build stays green and
somebody has to notice. REST is its first real reader. Phases 1 and 2 both touch
[PROTOCOL.md](PROTOCOL.md), which documents all three surfaces.

## What has to be true at the close

* `bash scripts/test-all.sh` green, and the count of checks higher than 95.
* **A test that fixes that a re-read `note` comes back with its body intact.** That is the heart of
  the increment; a test that only counts rows would pass against a recovery that returns husks.
* By hand against a running hub, **reading the answers and not their status codes** — in increment
  08 that is what uncovered a defect no record had. At least: a notice re-read after its mailbox
  was drained, and `replay` over the cap answering `invalid_replay` with a sentence that says so.
* The rules reconciled against the change and not only against the files it edited.

---

## Opening the next one

This file becomes the plan: what the increment is for, and one row per phase with its commit.
**Every phase is closed in the same commit that updates its row**, and the increment is closed by
moving the narrative to [specs/](specs/) and leaving this file as it is now.

## What is closest to due

Nothing is committed to, and this is not a ranking — it is where [backlog.md](backlog.md) says a
trigger is nearest, so the next decision is taken against something rather than from a blank page.
Each entry there names what has to become true first; read those rather than this list.

* **Two polls by the same agent both receive the same messages.** `Signal` wakes every waiter on
  the key, and the key is the agent name. The other half of the delivery finding this increment
  opens, deliberately left alone: it is a change to when a message is marked and to how a poll is
  woken, and putting it in the same commit as a recovery query would make a failure ambiguous
  about which half caused it.
* **The service installation has still never been run end to end.** Everything past
  `install-hub.ps1`'s administrator check is unexecuted. Due the next time the hub is installed as
  a service, which is also the first time somebody follows the README to the end.
* **`MessageStatus.Expired` is published and never produced.** The last of the three "published and
  unreachable" defects, and the only one that could not be closed in increment 08: removing a value
  of `MessageStatus` is breaking, so it waits for a `/v2`.
