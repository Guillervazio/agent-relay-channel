# H009 — Migrations are never applied from the application's startup path

## Context

Calling `Migrate()` during startup is one line and makes a fresh environment work immediately.

## Decision

Schema migration is a separate step — a bundle or a job run before the application starts. No
migration is applied from the application host.

## Consequences

Two replicas starting at once do not race to apply the same migration. The application does not
need DDL rights at runtime. A failed migration is a failed job with output, not a restart loop
that reports the application as unhealthy.

## What this does not authorise

It says nothing against automatic migration in a local development loop, where there is one
process and the failure mode is a developer reading an error. The rule is about deployment.

## Evidence outside this project

EF Core's own documentation warns against `Migrate()` at startup for multi-replica deployments and
for the permissions it requires.
