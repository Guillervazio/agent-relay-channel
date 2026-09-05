# Current work

## Increment 07 — a message that is not yours

`GET /v1/messages/{id}` and `GET /v1/threads/{id}` perform no authorisation at all. Any holder of
the token that knows an id reads the message body, and every thread listing hands ids out. It is
the oldest finding in [backlog.md](backlog.md), it is the reason
[P006](adr/P006-403-on-another-agents-mailbox.md) has to admit the confidentiality
[H011](adr/house/H011-404-not-403-when-authorisation-filters-rows.md) assumes does not exist here,
and it is the reason [architecture.project.md](../.claude/rules/architecture.project.md) names its
own exception instead of leaving "read-only projection" to taste.

**This increment does not give the channel confidentiality, and must not claim to.** One shared
token means the agent name is attribution and never authorisation
([P004](adr/P004-one-token-and-an-agent-header.md)), and `/v1/observe` deliberately serves every
body in the channel to any holder of that token. What closes here is a hole of a different class:
the two routes are the only place where an agent reading someone else's message is not even a
*mistake* the channel notices. The protection this buys is exactly the one the mailbox 403 buys —
against the honest error and the curious agent, never against a caller who is lying — and both
the record and the README have to say so in those words.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | The two reads become channel operations, and answer 404 to a stranger | done | this commit |
| 2 | The suites drive the refusal against a running hub, on all three surfaces | pending | — |

Phase 2 is separate because the defect being closed is one no unit test could have caught: the
routes were correct in isolation and wrong in composition, and what proves them fixed is a third
agent asking a real hub for somebody else's id.

### What each phase is for

**1.** `ChannelService` gains `MessageAsync(caller, id)` and `ThreadAsync(caller, threadId)`, and
`HubApp` and `ArcTools` call them instead of reaching into `MessageStore`. A message is readable
by its two ends and nobody else. A thread is **filtered to the caller's own rows** rather than
served whole to anyone who appears in it: a thread id is enough to join a thread by sending one
note, so participation cannot be what grants the history. When nothing survives the filter the
answer is the same 404 as a thread that never existed, **with the same detail text** — a
distinguishable message would leak through the wording what the status code exists to conceal.

The status is 404 and not the mailbox's 403, and that is H011 applied rather than departed from:
P006 buys its exception with a fact — `/v1/agents` already publishes every name — and that fact
has no counterpart here. A message id is 64 random bits and its existence is not public, so a 403
would confirm something the caller was not entitled to. No new error code: `not_found` already
means this.

[PROTOCOL.md](PROTOCOL.md) changes in the same commit, and so does the new record, **P016**.

**2.** `smoke.sh`, `smoke-cli.sh` and `smoke-mcp.sh` each get a third agent asking for the id of a
conversation it is not part of. The REST suite is where the 404 body is asserted; the CLI suite
asserts the exit code that comes with it; the MCP suite asserts `arc_thread` refuses in prose.
`/v1/observe` is checked to still serve the same conversation whole, because that is the deliberate
boundary and a suite that did not notice it disappearing would be worse than no suite.

### What this will make false

Listed now so the close is a check and not a search:

* **[P006](adr/P006-403-on-another-agents-mailbox.md)** ends by naming this as an assumption that
  is currently false in a worse way. That paragraph stops being true.
* **[architecture.project.md](../.claude/rules/architecture.project.md)** names `GET
  /v1/messages/{id}` and `GET /v1/threads/{id}` as two direct reads already on the wrong side of
  the projection line. They move to the right side, and the exception shrinks to the observer's
  reads alone.
* **[api-guidelines.project.md](../.claude/rules/api-guidelines.project.md)** has a status table
  with one row for 404 and a section arguing 403 over 404. Both need the distinction P016 draws.
* **[README.md](../README.md)** has three bullets under *What the shared token does not protect*.
  The third goes; the second becomes the honest statement of what is left.
* **[protocol.project.md](../.claude/rules/protocol.project.md)** lists what is breaking, and a
  **new refusal on a published route** is not on that list although narrowing `AgentNamePattern`
  is. Whether closing a confidentiality defect is a `/v2` is a question this increment has to
  answer rather than assume — the same question the backlog's `note`-to-oneself entry is waiting
  on.
* **[backlog.md](backlog.md)** loses the finding to the spec of this increment.

---

## Opening the next one

This file becomes the plan: what the increment is for, and one row per phase with its commit.
**Every phase is closed in the same commit that updates its row**, and the increment is closed by
moving the narrative to [specs/](specs/) and leaving this file as it was before.

## Last verified before this increment opened

5 September 2026, at the close of increment 06: `dotnet build` 0 warnings 0 errors; `dotnet test`
99 passed 0 failed 0 skipped; `dotnet format --verify-no-changes` clean; `dotnet restore --force`
clean; `bash scripts/test-all.sh` four suites green, 66 checks; `gate.yml` green on PR #6 and on
the merge; the same suite inside `mcr.microsoft.com/dotnet/sdk:10.0`, green; the container driven
by hand; `scripts/ArcHost.ps1` in Windows PowerShell 5.1. The full record is in
[specs/06-a-machine-that-is-not-this-one.md](specs/06-a-machine-that-is-not-this-one.md).
