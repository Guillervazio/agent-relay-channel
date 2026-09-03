# H004 — Contracts are their own project, records only

## Context

The types an API serialises can live in the application layer, or in the web project, or in a
project of their own. Each choice decides who is allowed to reference them.

## Decision

A separate contracts project holding records with no behaviour, no references to the domain, and
no references to the application layer. Persistence entities are never exposed through the API.

## Consequences

A client library can reference the contracts without dragging in the domain. Changing a contract
is visible as a change in that project rather than as a diff inside a handler. The cost is a
mapping step, which is also where a rename stops being a silent breaking change.

## What this does not authorise

A contract record is not a place for validation attributes, computed properties, or a constructor
that enforces an invariant. If a rule matters, it belongs to the command or the aggregate, where
it applies however the code is invoked.

## Evidence outside this project

The published-contract assembly pattern, and Clean Architecture's separation of the delivery
mechanism from the use cases.
