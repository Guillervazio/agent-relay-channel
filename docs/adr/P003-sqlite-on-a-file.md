# P003 — SQLite on a file, one connection per operation

## Context

The channel has to survive a hub restart: a question asked before the restart must still be in the
mailbox afterwards. The hub runs on somebody's desktop, installed by a PowerShell script.

## Decision

SQLite on a single file, opened per operation, in WAL mode. No server, no container, no separate
process to operate.

## Consequences

Backup is copying a file. There is no connection string to configure and no second thing to
install. WAL lets the observer's reads run alongside the single writer.

`Arc.Core` is both the model and the store, which is why the base build rule about a
package-free domain project has no subject here.

## What this does not authorise

One writer. The design assumes a single hub process owning the file, and that assumption is what
makes the missing `WHERE status <> 'answered'` in `AddResponseAsync` a bug rather than a
catastrophe. Two hubs against one file over a network share is not a supported configuration and
would need this record reopened.
