# H011 — Where authorisation filters rows, the single-resource route answers 404

## Context

When a listing returns a different set of rows per caller, the single-resource route for a row
outside that set has two plausible answers: 403, which is literally true, or 404, which is what
the listing already implied.

## Decision

404. "No such resource" and "not yours" are deliberately indistinguishable from outside, and one
exception raises both.

## Consequences

There is no route that confirms the existence of a resource a caller may not see. The listing and
the single-resource path tell one consistent story.

## What this does not authorise

It does not turn every 403 into a 404. Where a policy governs the endpoint as a whole — the answer
is the same for everyone the policy admits — a refusal is a refusal and 403 is correct. This is
only for the case where authorisation is selecting rows.

## Origin

Derives with `PlastipackInventoryApp`'s P008 from one earlier entry, 0021,
split into the criterion and its application.

## Evidence outside this project

Standard practice for authorisation that is row-scoped: not confirming the existence of a resource
to someone not entitled to it. OWASP's guidance on insecure direct object references says the same
thing from the attacker's side.
