# P001 — A blocking long poll, not a message broker

## Context

Two CLI agents from different providers, on different machines, need to ask each other questions
and get answers. A CLI agent only exists during its turn: it cannot hold a subscription, cannot
run a consumer loop, and cannot be called back after it returns.

## Decision

The channel is HTTP long polling against a small hub. `ask` blocks the caller's turn until the
other side answers or the wait elapses. No broker, no queue, no daemon on either agent's machine.

## Consequences

The agent needs nothing but an HTTP client, which every one of them already has. A question and
its answer are one request each, so a transcript shows the exchange in order.

Waiting costs a held connection, which is why Kestrel's keep-alive is derived from `ARC_MAX_WAIT`
rather than left at its default — see `.claude/rules/concurrency.project.md`.

The state that makes debugging possible is the `waiters` snapshot: two agents each waiting on the
other is visible at a glance in `/v1/observe`, which is not true of a queue you have to drain to
inspect.

## What this does not authorise

This is not an argument against brokers. It is an argument about a client that does not exist
between turns. The day something on the other end is a long-running service, the reasoning above
stops applying to it.
