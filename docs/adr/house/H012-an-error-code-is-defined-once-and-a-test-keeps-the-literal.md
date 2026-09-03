# H012 — An error code is defined once, and the test that guards it keeps the literal

## Context

An error code is published contract: a client branches on it. Duplicated across an exception and a
controller, two copies can drift with nothing to catch it. The obvious fix is a shared constant —
and the obvious next step, referencing that constant from the tests, is wrong.

## Decision

One definition, in the one project everything else references. Tests spell the literal out and do
**not** reference the constant.

## Consequences

Changing the constant's value fails the test, which is exactly the breaking change the test exists
to catch. The duplication in the test is the point: the literal there stands in for the client's
copy of the contract.

## What this does not authorise

It is not a general licence for magic strings in tests. The rule is narrower and has a test of its
own: a test freezes a value when the test **is** the client, and discovers the value when the
test's whole job is to detect divergence between two copies.

## Evidence outside this project

"Do not test a constant against itself" — the standard argument against asserting on the same
symbol the production code reads.
