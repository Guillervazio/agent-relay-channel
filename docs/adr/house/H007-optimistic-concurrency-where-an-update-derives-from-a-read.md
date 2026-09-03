# H007 — A concurrency token is for an update derived from a previous read

## Context

Adding a version column everywhere is uniform and costs a column. Adding none is simpler and
loses a class of lost update. Neither answers which tables need one.

## Decision

A table carries a concurrency token when an update is **derived** from what was read — a balance
incremented, a counter advanced, a collection appended to after loading it. A table whose update
replaces every editable field with values the caller sent outright does not need one: last-write-
wins leaves a coherent row somebody actually asked for.

## Consequences

The decision is made per table, in its configuration, with the criterion written beside it. A
reader can tell whether the absence of a token was decided or forgotten.

## What this does not authorise

It is not an argument against pessimistic locking in general, and it does not license a retry
decorator that hides contention instead of surfacing it. A conflict that reaches the caller is
information.

## Origin

Derives with `PlastipackInventoryApp`'s P007 from one earlier entry, 0008,
split into the criterion and its application.

## Evidence outside this project

Optimistic concurrency control predates every ORM that implements it; the read-modify-write
criterion is the one the technique is defined against.
