# P013 — The API version is a literal path segment

## Context

The wire needs a version. The options are a library that negotiates one, a header, or a segment
written into the route.

## Decision

`/v1/…`, typed into the route, with no versioning library. A breaking change takes a `/v2` prefix
and new handlers; the old ones freeze rather than branch.

`/healthz` sits outside the version.

## Consequences

The version a request used is visible in an access log with nothing decoded. There is no
negotiation to reason about and no package to approve.

`/healthz` is outside because the **consumer** decides: a supervisor or an installer probes a
fixed path and cannot be told about a new one. Versioning the probe would be versioning something
nobody can upgrade.

## What this does not authorise

Branching inside a handler on a version. Freezing means the `/v1` handler keeps behaving as `/v1`;
an `if (version == 2)` inside it is the thing this record refuses.

## Origin

Recorded as `P` and flagged as a promotion candidate. `PlastipackInventoryApp` took the same
decision independently, for the same reason including the health-probe argument, as its P012 —
which is exactly the second-project condition
[house/README.md](house/README.md) sets for promoting a `P` to an `H`. Promoting it is a decision
for the package, not for this repository, and it is in `docs/backlog.md`.
