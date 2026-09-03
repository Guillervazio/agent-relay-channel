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

It also rests on an assumption that is currently false in a worse way. `GET /v1/messages/{id}` and
`GET /v1/threads/{id}` perform **no authorisation at all**: any authenticated agent that knows an
id reads the message. That is a finding in `docs/backlog.md`, not part of this decision — and
closing it does not change this record, because `/v1/agents` will still publish the names.
