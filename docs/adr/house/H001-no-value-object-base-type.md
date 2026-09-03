# H001 — No `ValueObject` base type

## Context

A domain layer needs value objects with structural equality. The traditional answer is an
abstract `ValueObject` base class that implements `Equals` and `GetHashCode` over an enumerable
of components each subtype supplies.

## Decision

Value objects are records. No `ValueObject` base type exists, and none is added.

## Consequences

A record already carries the structural equality the compiler writes, so a base type would either
sit unused — dead code — or force rewriting the records to hand-implement worse equality. The
absence is a decision, not a gap, and the domain's `Common/` folder says so where a reader would
otherwise notice the omission.

## What this does not authorise

It says nothing about records for entities. An entity has identity rather than structural
equality, and giving one a record's `Equals` is how two different customers with the same name
become the same customer.

## Evidence outside this project

The C# records feature (C# 9) exists for exactly this shape, and the compiler-generated equality
is specified rather than incidental.
