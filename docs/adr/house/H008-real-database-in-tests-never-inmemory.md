# H008 — Database-backed suites run against a real database in a container

## Context

Tests that touch persistence can use an in-memory provider, a shared developer database, or a
container started per test run.

## Decision

A real database in a container, one container per test project, with data reset between tests.
The in-memory provider is never used.

## Consequences

The suites catch what only a real engine says: provider-specific translation failures, constraint
violations, the precision a column actually stores. They need the container runtime available,
which is a documented prerequisite rather than a surprise.

## What this does not authorise

Unit tests still touch nothing. This is about the suites whose subject **is** persistence, and
turning every test into an integration test is the failure mode on the other side.

## Evidence outside this project

Microsoft's own guidance advises against the EF Core in-memory provider for testing, on the
grounds that it is not a relational database and does not behave like one.
