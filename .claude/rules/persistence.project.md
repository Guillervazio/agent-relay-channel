---
paths:
  - "src/Arc.Core/MessageStore.cs"
  - "tests/**/*.cs"
---

# Persistence — this project

**This area has no shared base.** `dotnet-house`'s `shared/entity-framework.md` has no subject
here: there is no ORM, no `DbContext`, no migrations tool and no configuration classes. What
follows replaces it entirely.

## SQL is written, parameterised, and lives in one file

Every statement is in `MessageStore` and every value is a parameter. No interpolation into a
command string, ever — not even for a value that "obviously" cannot contain a quote, because the
next caller is the one that makes it wrong.

No other type opens a connection. A caller that needs data asks `MessageStore` for it.

## One connection per operation

`OpenAsync` per call, disposed at the end. The pool reuses the handle, so this is not the cost it
looks like, and it is what keeps a long poll from holding a connection for five minutes.

## The three connection settings are decisions

`journal_mode=WAL`, `synchronous=NORMAL` and `DefaultTimeout=30` each buy something specific:
concurrent readers alongside the single writer, a durability trade that survives a process crash
but not a power cut, and a wait rather than an immediate `SQLITE_BUSY` under contention. **None of
them is a default, and none changes without a record.**

## A write whose correctness depends on a read carries the check in the same statement

This is [H007](../../docs/adr/house/H007-optimistic-concurrency-where-an-update-derives-from-a-read.md),
and **`AddResponseAsync` violates it today.** `ChannelService.RespondAsync` reads the request,
checks `Status != Answered` in C#, and then the store runs:

```sql
UPDATE messages SET status = 'answered', answered_at = $answered_at
 WHERE id = $id AND kind = 'request'
```

with no condition on status. Two responders that both pass the C# check both insert a response
row and the request ends up with two answers. The check belongs in the `WHERE`, with the rows
affected inspected and the transaction rolled back when it is zero. Recorded in `docs/backlog.md`.

The general rule: if the decision to write came from a row you read, the `WHERE` repeats it.

## Schema changes are additive

`CREATE TABLE IF NOT EXISTS` and `CREATE INDEX IF NOT EXISTS`, run by `InitializeAsync` at hub
startup — which contradicts
[H009](../../docs/adr/house/H009-migrations-never-run-at-application-startup.md) knowingly, for
the reasons in [P007](../../docs/adr/P007-the-schema-is-created-at-startup.md).

The first change that is **not** additive — a dropped column, a renamed table, a narrowed type —
ends this scheme, and it is exactly the point at which P007 has to be reopened rather than worked
around. An `IF NOT EXISTS` that silently does nothing against an older database is the failure
this rule exists to prevent.

## The store's vocabulary

`MessageStore` returns records and `null`. It throws `ArgumentException` for a caller that broke
its contract, and it **never throws `ChannelException`** — status codes and error codes are
`ChannelService`'s vocabulary, and a store that knew them would be a store that knew it was being
called over HTTP.

## Timestamps

Stored as text in a fixed round-trip format, read back with `DateTimeOffset`. A value that made
the round trip is not bit-identical to the one that went in, so a test comparing one side from
the database against one side from memory asserts with a tolerance and says what the tolerance is
distinguishing. Two in-memory values are compared exactly.
