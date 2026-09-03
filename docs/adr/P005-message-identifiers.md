# P005 — Identifiers are a readable prefix plus an opaque suffix

## Context

Every message carries an id that appears in logs, in the observer panel, in an agent's prose, and
in the `correlation_id` of the reply. [H010](house/H010-sequential-identifiers.md) asks for
sequential GUIDs so inserts land at the end of an index.

## Decision

`req_`, `res_`, `not_` or `thr_`, followed by an opaque, time-ordered suffix. The prefix is for a
human reading a log; nothing parses it.

## Consequences

A person seeing `req_a1b2…` in a transcript knows what kind of thing it is without a lookup, and
the ordering keeps the primary index appending rather than scattering.

## What this does not authorise

**Truncating a UUIDv7.** Today the suffix is `Guid.NewGuid().ToString("n")[..16]` — 64 bits of
randomness, collision-free at any volume ARC will see. The first 16 hex characters of a UUIDv7 are
48 bits of millisecond timestamp, 4 of version and **12 of randomness**: 4096 values inside one
millisecond, with birthday collisions starting around 64 identifiers in the same millisecond,
against a `TEXT PRIMARY KEY`. Applying H010 by swapping the generator and keeping the truncation
would make this **worse**. Use the full 32-character form, or a base32 encoding of the same 128
bits.

Changing the length is visible on the wire, so it is measured against
`.claude/rules/protocol.project.md` first, not assumed to be invisible.

It also does not reach `WaiterRegistry` or `EventStream`. Their `Guid.NewGuid()` calls are
process-local dictionary keys that never enter an index and never leave the process. H010 is about
identifiers in a table.
