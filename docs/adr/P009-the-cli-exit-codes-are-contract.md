# P009 — The CLI's exit codes are published contract

## Context

`docs/AGENTS.md` tells an agent to branch on `arc`'s exit code rather than parse its output: 3
means the wait elapsed and the question is still live, 4 means there was nothing to collect. A
number an agent branches on is an interface.

They were declared as `const int` locals in a top-level program, where the naming rule read them
as locals and asked for camelCase.

## Decision

They live in `Arc.Cli.ExitCodes`, a static constant table, each with the sentence that says what
it means to a caller. A code never changes meaning; a new state takes a new number.

## Consequences

The one thing the CLI publishes now looks like the contract it is, and IDE1006 is satisfied
without renaming a contract to look like scratch state.

## What this does not authorise

A test asserting `ExitCodes.Timeout` against `ExitCodes.Timeout`. Per
[H012](house/H012-an-error-code-is-defined-once-and-a-test-keeps-the-literal.md), the test spells
`3`: a test written against the constant passes through exactly the change that breaks every
caller. No such test exists yet — `docs/backlog.md`.
