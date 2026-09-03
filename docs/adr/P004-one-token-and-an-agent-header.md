# P004 — One shared token authenticates; the agent name is a header

## Context

The hub sits on a LAN between machines their owner controls. It needs to keep out everything that
is not one of those agents, and it needs to know which agent is speaking.

## Decision

`X-ARC-Token` is one shared secret, compared in fixed time, and it is what authenticates.
`X-ARC-Agent` says who is speaking and is **not** a credential. Anonymous mode
(`ARC_ALLOW_ANONYMOUS=1`) exists for local development and binds loopback only.

## Consequences

Setup is one secret, distributed once. There are no per-agent keys to rotate.

Because any holder of the token can present any agent name, **the agent name is attribution, not
authorisation**. The 403 on another agent's mailbox stops an honest mistake and a curious agent;
it does not stop a dishonest one, and it was never able to.

## What this does not authorise

Any decision that must hold against a caller who is lying about their name. If ARC ever carries
something one agent must not read even when it wants to, this record is what has to change first —
per-agent credentials, not a stricter check on a header.
