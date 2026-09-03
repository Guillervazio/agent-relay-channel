# H002 — An interface with one implementation needs a reason that is not symmetry

## Context

Every service can be given an interface, and a codebase that does it uniformly looks consistent.
Most of those interfaces have one implementation and nothing that substitutes them.

## Decision

An interface for a type with a single implementation is justified only when it removes a
constraint that cannot be removed otherwise — a type parameter that has nowhere to go, or a
dependency a test genuinely has to replace. Not by symmetry with its neighbours.

## Consequences

Concrete classes are injected where nothing substitutes them. The interfaces that survive are the
ones a reader can ask a question about and get an answer.

## What this does not authorise

It is not a ban. The exception this rule was tested against is a non-generic interface over a
generic base class, where the alternative is reflection over a closed generic or a second
collection kept in step by hand. That is a constraint, not a preference — and the test to apply
is whether removing the interface has a cost you can name.

## Evidence outside this project

YAGNI, and the long-standing argument against header-interface-per-class. Predates this project by
decades.
