# H013 — 422 for a well-formed request that fails validation, 400 only for one that cannot be read

## Context

Both status codes are used for "the request was rejected", and picking per endpoint leaves a client
unable to tell the two situations apart.

## Decision

400 when the request cannot be interpreted at all — malformed body, a value that cannot bind to its
parameter's type. 422 when it was understood and a rule refused it.

## Consequences

A client can branch: 400 means its serialisation is wrong, 422 means its data is. The 422 body
names the fields; the 400 body cannot, because the fields were never parsed.

## What this does not authorise

422 is not a bucket for every refusal. A conflict with existing state is 409, and a rule the caller
is not entitled to trigger is an authorisation answer, not a validation one.

## Origin

Derives with `PlastipackInventoryApp`'s P011 from one earlier entry, 0005, split
into the criterion and its application.

## Evidence outside this project

The semantics are in the HTTP specification for 422 (originally WebDAV, now part of the core HTTP
semantics registry) and predate this project.
