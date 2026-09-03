# H010 — Identifiers are sequential GUIDs

## Context

A GUID primary key is convenient — the application can assign it before a round trip — and a
random one fragments the clustered or primary index it lands in.

## Decision

Identifiers are generated with a sequential GUID (`Guid.CreateVersion7()`), never a random one,
and the database is told never to generate them itself.

## Consequences

Inserts land at the end of the index rather than scattered through it, so page splits stay rare on
a table that only grows. The identifier is still assignable before the entity is saved.

## What this does not authorise

Sequential does not mean guessable-is-fine. An identifier that must not be enumerable by an
outside caller is a separate concern, and a version-7 GUID leaks a timestamp.

## Evidence outside this project

The index-fragmentation argument predates this by years — `NEWSEQUENTIALID` in SQL Server exists
for it — and UUIDv7 is the standardised form of the same idea.
