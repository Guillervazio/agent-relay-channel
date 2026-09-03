# H006 — Repositories expose aggregate roots; cross-aggregate reads use query objects

## Context

"Aggregate roots only" is a rule about repositories. Taken literally it leaves no way to answer a
read whose result spans two aggregates without either a second call per row or a navigation
property between aggregates.

## Decision

Repositories keep returning aggregate roots. A read that spans aggregates goes through a query
object that is not a repository, returns a projection, and never returns something saveable.

## Consequences

The rule keeps protecting what it was for — an invariant can only be broken by something that
mutates, so a join returning no aggregate costs nothing. The listing gets its columns in one
round trip.

## What this does not authorise

A repository method returning a shape that spans two aggregates is still the breach, because it
hands a caller something it might try to save. And a query object may never be the thing that
loads an aggregate a use case then mutates.

## Evidence outside this project

DDD's aggregate rule is stated about consistency boundaries and writes; CQRS's read side is the
standard answer for reads that do not fit them.
