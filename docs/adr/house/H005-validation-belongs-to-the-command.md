# H005 — Validation is declared against the command, not against the contract

## Context

A request arriving over HTTP could be validated where it binds — on the contract record — or one
layer in, on the command the handler receives.

## Decision

Validators are written against the command or the query. The contract carries no validation.

## Consequences

The rule applies however the use case is invoked: over HTTP, from a background job, from a test
that constructs the command directly. A second transport gets the same validation without anybody
remembering to copy it.

## What this does not authorise

It does not move business invariants out of the domain. A validator answers "is this request
well-formed enough to attempt", and the aggregate still answers "may this be true". Both exist,
and a rule that guards the existence of an entity belongs to the entity.

## Evidence outside this project

Standard CQRS practice: the command is the use case's input, and its contract is the thing worth
constraining.
