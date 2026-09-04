# P007 — The schema is created when the hub starts

## Context

[H009](house/H009-migrations-never-run-at-application-startup.md) forbids applying migrations from
application startup: replicas race each other, the application needs DDL permissions it should not
have, and a failure turns into a restart loop.

`HubApp.BuildAsync` calls `await store.InitializeAsync()` before returning the application.

## Decision

ARC does it at startup anyway.

## Consequences

Installation is copying a binary and running it. There is no migration step to forget, no bundle
to build, and no second thing that has to be at the same version as the first.

The three failure modes H009 names do not exist here: there is one process, not replicas; the
process owns the file outright, so DDL permission is not a separate grant; and the statements are
`CREATE TABLE IF NOT EXISTS`, so a second start is a no-op rather than a failure.

## What this does not authorise

Surviving the first non-additive schema change. `IF NOT EXISTS` does nothing against an existing
older table, which means a dropped column or a narrowed type would leave a database silently on
the old shape with a hub that believes otherwise. That is the point at which this record is
reopened, and it is named in `.claude/rules/persistence.project.md` so the change cannot be made
without meeting it.

It also does not authorise a second hub against the same file.
