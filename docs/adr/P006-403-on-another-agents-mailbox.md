# P006 — Reading another agent's mailbox answers 403, not 404

## Context

[H011](house/H011-404-not-403-when-authorisation-filters-rows.md) says that where authorisation
filters rows, the single-resource route answers 404: a 403 confirms the resource exists, which is
information the caller was not entitled to.

`InboxAsync` and `AwaitResponseAsync` raise 403 when an agent asks for something that is not
theirs.

## Decision

ARC keeps the 403.

## Consequences

An agent that mistypes its own name gets told it is not allowed, which is the true answer, rather
than being told nothing is there.

The reason this does not weaken anything is a fact about this code, not a preference: `/v1/agents`
publishes every agent id to every authenticated caller, so existence is already public. A 404
would conceal nothing while making a common mistake harder to diagnose.

## What this does not authorise

It is not a general licence to prefer 403. H011 stands wherever existence is not already public,
and the moment `/v1/agents` becomes scoped, this record has to be reopened.

It is also not the answer for a route whose subject is not a mailbox. `GET /v1/messages/{id}` and
`GET /v1/threads/{id}` answer **404** to an agent they are not addressed to, because a message id
is not published anywhere and a 403 would confirm that it exists —
[P016](P016-a-message-is-read-by-its-two-ends.md). Those two routes performed no authorisation at
all until increment 07, which is the state this paragraph used to record; closing it did not
change the decision above, because `/v1/agents` still publishes the names.
