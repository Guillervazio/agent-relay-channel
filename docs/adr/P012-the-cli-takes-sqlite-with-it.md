# P012 — The CLI references Arc.Core, and inherits SQLite with it

## Context

[H004](house/H004-separate-contracts-project.md) says the records that cross the wire live in a
project of their own, so a client does not take the server's dependencies to use them.

`Arc.Cli` references `Arc.Core` for four records — `Message`, `AskResult`, `InboxResult`,
`ErrorBody` — and inherits `Microsoft.Data.Sqlite` and the native SQLite bundle into a binary that
never opens a database.

## Decision

Accepted for now. No `Arc.Contracts` project.

## Consequences

The published CLI carries a native library it does not use. Nothing else: it does not open a
connection, and the dependency is dead weight rather than a risk surface that matters.

A third project for four records is real cost — a solution entry, a reference to keep straight,
and a place for the next person to wonder about.

## What this does not authorise

Adding a fifth thing to `Arc.Core` because the CLI already references it. The dependency is
tolerated, not endorsed.

**Due when:** the published CLI's size or dependency surface is questioned. Measure first —
`dotnet publish src/Arc.Cli -c Release -r win-x64`, with and without — because the argument for
splitting is a number nobody has taken.
