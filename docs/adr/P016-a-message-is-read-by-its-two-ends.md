# P016 — A message is read by its two ends, and a stranger gets a 404

## Context

`GET /v1/messages/{id}` and `GET /v1/threads/{id}` were served with no authorisation at all: any
holder of the token that knew an id read the body. Ids are 64 random bits, which is obscurity and
not access control, and every thread listing hands them out.

Two questions had to be answered together, because answering only the first would have produced a
route that refuses correctly and still leaks.

## Decision

**A message is readable by its sender and its recipient, and by nobody else.** A message that is
not yours and a message that does not exist answer the **same 404 with the same body** — not the
403 `InboxAsync` raises.

**A thread is trimmed to the caller's own messages in it**, rather than served whole to anyone who
appears in it. When nothing survives the trim the answer is that same 404.

Both reads move behind `ChannelService`, where all three surfaces meet them.

## Consequences

The 404 is [H011](house/H011-404-not-403-when-authorisation-filters-rows.md) applied, and it does
not reopen [P006](P006-403-on-another-agents-mailbox.md): P006 buys its exception with a fact —
`/v1/agents` already publishes every agent name, so a 404 on a mailbox would conceal nothing — and
that fact has no counterpart here. A message id is not public, so a 403 would confirm something
the caller was not entitled to know. Two routes answering 403 and two answering 404 is one rule
applied to two different facts, not an inconsistency.

Trimming the thread rather than granting it to participants is what makes the first half hold.
Any agent can join a thread by sending one note with its `thread_id`, so participation is
self-granted: had it been the test, the refusal on `/v1/messages/{id}` would have been one note
away from being undone.

The detail text is part of the decision. A 404 whose prose differs between "no such thing" and
"not yours" answers, in words, exactly the question the status code was chosen not to answer —
which is why the tests compare the whole body and not the code.

`ArcTools.ThreadAsync` keeps answering in prose rather than throwing, and its one sentence covers
both cases for the same reason.

## What this does not authorise

**Any claim that the channel is confidential.** It is not, and three things that make it not so
are unchanged and deliberate:

* One shared token, and an agent name that is attribution and never authorisation —
  [P004](P004-one-token-and-an-agent-header.md). A caller willing to present somebody else's name
  reads their messages, and this record does not touch that.
* `/v1/observe/history` and `/v1/observe/threads` serve **the whole channel, with bodies**, to any
  holder of the token, and do not even ask for an agent name. That is what the panel is, and
  [P010](P010-the-observer-page-is-unauthenticated.md) is why. A suite asserts it still does.
* Nothing is encrypted at rest or in transit by the hub itself.

What closes here is narrower and worth having on its own: reading another agent's message was the
one case the channel did not notice at all, so a mistake could not be told from an intrusion, and
the routes that leaked were the same ones an honest agent uses. It now stops an honest error and a
curious agent — the same class of protection as the mailbox 403, and no more.

**If ARC ever has to carry something one agent must not read even when it wants to, this record is
not the one that changes.** P004 is, and the change is per-agent credentials.
