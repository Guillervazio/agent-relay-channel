# Increment 09 — a note you can read twice

A `note` whose mailbox response was lost in transit was unrecoverable. `InboxAsync` marks the rows
`delivered` and *then* returns them, so a response that never arrives has already emptied the
default mailbox — and `?unanswered=true`, the only recovery path the channel published, ended in
`AND kind = 'request'`. The channel had a message type whose loss was permanent and undetectable.

The clause was not the whole defect, and deleting it would have been a worse one.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | The mailbox can be re-read within a window: `Arc.Core`, REST, and `PROTOCOL.md` in the same commit | done | `7155367` |
| 2 | MCP and the CLI carry it, and the handshake says it exists | done | `a60850c` |
| 3 | The suites drive it against a running hub | done | `62ad86e` |
| 4 | Close: the record, the rules it made false, and what the backlog no longer owes | done | this commit |

Verified at close (6 September 2026): `dotnet build` **0 warnings, 0 errors**; `dotnet test`
**133 passed, 0 failed, 0 skipped**; `dotnet format --verify-no-changes` clean; `dotnet restore
--force` clean, no advisory; `bash scripts/test-all.sh` **four suites green, 114 checks**
(45 REST, 28 CLI, 30 MCP, 11 UI), up from 95.

Verified by hand against a running hub on `:8803`, reading the answers rather than their status
codes. On REST the replayed notice came back with its body intact — accents and all — and with
`"status": "delivered"`; a second replay returned it again unchanged; the default mailbox stayed
`204` throughout; `replay=90000` and `replay=-5` both answered `invalid_replay` with the sentence
naming the range; `replay=86400` answered `200` and `replay=0` answered `204`. Over MCP the same
refusal reached the model as `invalid_replay: 'replay' va entre 0 y 86400 segundos.` with
`isError: true`, and the recovered notice arrived inside the prose `arc_inbox` writes. On the CLI,
`inbox` exited 4, `inbox --unanswered` exited 4, `inbox --replay 60` exited 0 and printed the body
with its accents intact, and `inbox --replay 90000` exited 1 saying
`El hub respondió 422 (invalid_replay)`. A replay on the *sender's* mailbox answered `204`: the
window returns what was addressed to you, not what you sent.

Increment 06's hosting verification — the container, the SDK image, Windows PowerShell 5.1, the
installer's reachable half — was not re-run and is unaffected: nothing here touches how the hub is
started.

---

## What the plan got wrong

| The plan said | What was true |
|---|---|
| Four phases, and phase 1 would be core plus REST because a store parameter with only a test to read it is unused code | Right, but for a reason the plan did not have. The MCP call site passed its `CancellationToken` positionally, so appending the parameter *there* would have compiled silently into the token's slot. Putting `replay` after `wait` rather than before it is what turned every stale call site into a compile error instead of a changed meaning — and it left the existing four-argument test calls meaning what they said |
| The CLI would validate `--replay` before sending it | It forwards the value untouched. Judging the range in the CLI would put two opinions about it in the codebase and publish only one, and the CLI would be answering for a rule it does not own. `--replay 90000` is a round trip to the hub on purpose |
| The rules reconciliation would be about the protocol appendix, since this changes the wire | The protocol appendix needed nothing: it already says that adding an error code and adding an optional parameter are not breaking. The false clause was in `api-guidelines.project.md`, in a file this increment never edited, and it was **paging** — "the limits are fixed server-side rather than parameters: the inbox is one agent's". Read literally it forbade the fix |
| Three decisions, taken before any code | Three, and all three held. But the second carried a fourth nobody asked about: with no `delivered_at` column and P007 forbidding one, the window measures *creation* and not delivery. For the case it serves those coincide, and the record has to say so rather than let a reader assume otherwise |

## What was decided

[P020](../adr/P020-a-recovery-window-not-a-state.md), in four parts: a new parameter rather than a
widened one, because a request drains itself by being answered and a note never does; a window in
seconds counted from the hub's own clock, because the response a caller lost is exactly what would
have carried the ids and the timestamps; refused rather than clamped, with its own code so a
caller passing two bounds learns which one was refused; and pure — re-reading writes nothing,
because marking before the client has it is the defect, and a recovery that consumed itself would
be that same defect with nothing behind it.

The line P001 draws was the one to stay behind, and the check is mechanical: the hub re-attempts
nothing, keeps no per-message delivery state, and is told nothing by the client. What confirmed
the diagnosis was noticing that `/v1/observe/history` had been serving these notices all along —
the data was never lost from the database, only from the mailbox, which makes this a gap in a
query surface rather than in delivery.

## The rules it made false

Six clauses were checked and three were wrong; the costliest was in a file the change never
opened.

* **`api-guidelines.project.md`, *Paging*.** Claimed no listing takes a caller-supplied bound,
  because "the inbox is one agent's". Both halves are now false, and the second was never a bound
  at all: one agent's mailbox grows without limit once part of it stops draining. Rewritten, with
  the boundary that `replay` selects *which* messages are in the answer and is not a licence for a
  `limit`, an `offset` or a cursor.
* **`api-guidelines.project.md`, *Long polls*.** Stated the refuse-don't-clamp rule about `wait`
  alone. There are two such parameters now, and the reasoning was never about waiting — it is that
  a silently narrowed request is indistinguishable from an honest empty answer. Generalised, with
  the boundary that a value the caller can sensibly mean (`wait=0`, `replay=0`) is still accepted.
* **`persistence.project.md`, *Timestamps*.** Said the format matters for the round trip. It is
  now load-bearing for comparison too: `created_at >= $since` is text against text, correct only
  because every row goes through `Format` as fixed-width UTC.

Three more were checked and stand: adding an error code and an optional parameter are already
covered by `protocol.project.md` as not breaking, the schema change is additive as
`persistence.project.md` requires, and the operation reaches all three surfaces as
`architecture.project.md` demands.

Outside the rules: `CLAUDE.md` said 119 tests, the README's command table did not know `--replay`
existed, and neither `README.md` nor `docs/AGENTS.md` told an agent what to do about a notice it
thought it had missed — the MCP handshake did, which left the CLI's reader as the only one in the
dark.

## What this increment did not fix, deliberately

The marking is still in the wrong order, and a first read still reports `status: "pending"` for a
row that is already `delivered`. Two simultaneous polls by the same agent still both receive the
same messages. Both stay in [backlog.md](../backlog.md): they are changes to *when* a message is
marked and *how* a poll is woken, and putting either in this increment would have made a failure
ambiguous about which half caused it. `replay` is a way out of the consequence, not a repair of
the cause, and the backlog entry now says exactly that.
