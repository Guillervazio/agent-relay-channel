# Increment 08 — promises the code did not keep

Two entries in [backlog.md](../backlog.md) had been waiting on the same thing: somebody to decide
what the contract actually says. `PROTOCOL.md` called `refs` "a free-form JSON object" and nothing
checked that it was one. `AskAsync` refused a request to oneself with `self_addressed` while
`NoteAsync` allowed the same thing, with no comment, rule or record saying why.

A third came out of verifying those two against a running hub, and it is the reason all three are
in one increment: **no refusal reached the model over MCP at all**. Every `ChannelException` came
back as `An error occurred invoking 'arc_note'.` — same sentence for four different codes, in
English, with neither code nor reason.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | `refs` is any JSON value, and `invalid_refs` says which surface can emit it | done | `3f46a3e` |
| 2 | An agent may queue a question to itself, and may not wait on it | done | `61ebf82` |
| 3 | MCP says why it refused, instead of that something went wrong | done | `655ecca` |
| 4 | The suites drive all three against a running hub | done | `67599c3` |
| 5 | Close: the records, the rules they made false, and what the backlog no longer owes | done | this commit |

Verified at close (5 September 2026): `dotnet build` **0 warnings, 0 errors**; `dotnet test`
**119 passed, 0 failed, 0 skipped**; `dotnet format --verify-no-changes` clean; `dotnet restore
--force` clean, no advisory; `bash scripts/test-all.sh` **four suites green, 95 checks** (37 REST,
22 CLI, 25 MCP, 11 UI).

Verified by hand against a running hub on `:8799`, reading the answers rather than their status
codes: `arc_note` with broken `refs` came back as
`invalid_refs: 'refs' debe ser JSON válido: …`, and `arc_ask` to oneself with `wait: 5` as
`self_addressed: Un agente no puede esperar su propia respuesta. Con 'wait' a 0 la petición queda
en tu buzón.` Both had answered `An error occurred invoking 'arc_x'.` on the same hub before the
filter, which is how the third finding was found in the first place.

Increment 06's hosting verification — the container, the SDK image, Windows PowerShell 5.1, the
installer's reachable half — was not re-run and is unaffected: nothing here touches how the hub is
started.

---

## What the plan got wrong

| The plan said | What was true |
|---|---|
| Three phases: two findings and a close | Five. The third decision was not in the plan because it was not in the backlog — it was found by running the first two against a real hub, which the plan had scheduled *after* the code as a formality |
| Closing `invalid_refs` meant documenting which surface can emit it | It meant discovering that the surface which emits it discards it. The finding said "REST cannot emit it"; the truth was worse and adjacent — MCP emitted it and threw it away, along with every other code |
| `self_addressed` was one decision: allow it in `ask`, or forbid it in `note` | Two. *Addressing* yourself is what a note already did and nothing was wrong with it; *waiting* on yourself is structurally impossible. Treating them as one is why the backlog entry read as a coin flip, and splitting them is what let `self_addressed` narrow instead of being left published with nothing to emit it |
| The MCP fix would be a `try`/`catch` per tool, or nothing | A `CallToolFilter`, one registration for all seven. The SDK's own documentation says those filters wrap the handler for a tool "that isn't found", which reads as excluding registered tools — it does not, and compiling was how that got settled rather than reading |
| The suites would confirm the work | One of them found the work. `smoke-mcp.sh`'s new check was written to assert a message the surface could not produce, and it failed — which is the only reason any of phase 3 exists |
| Two rule clauses were made false, and both were in files this change edited | Six, across five files, and the costliest was in one nothing here touched. `concurrency.project.md` derives `MaxRequestBodySize` from `MaxBodyBytes * 2` and says *"the factor of two is the JSON around the body, not slack"* — which uncapping `refs` turned into published room for `refs`, and turned that row's *What breaks otherwise* column into a description of what now happens rather than what it prevents |

## What was decided

**[P017](../adr/P017-refs-is-any-json-value.md)** — `refs` is any JSON value, stored verbatim, with
the object as convention. `invalid_refs` drops the claim about shape and keeps its code, reachable
over MCP alone because that is the only surface where `refs` arrives as a string of its own.

**[P018](../adr/P018-an-agent-may-queue-work-for-itself.md)** — an agent may address a request to
itself and may not wait on it. The refusal sits in both doors a wait can be asked for, and after
the lookup for an existing response, so collecting an answer one gave oneself is never refused.

**[P019](../adr/P019-a-refusal-a-model-cannot-read-is-not-a-refusal.md)** — a `CallToolFilter`
translates `ChannelException` into `«code»: «detail»`. The code leads because the code is what does
not change meaning.

**Widening the document is the default repair when tightening would be breaking.**
[protocol.project.md](../../.claude/rules/protocol.project.md) had the test for a new refusal since
increment 07 but named no alternative, so a contract that promises more than the code enforces
looked like it needed a `/v2`. It does not: stating what the code has always done breaks nobody.
The clause now says so, and says what it does not authorise — the document is not the half that
always yields, and where it is the *narrower* of the two the repair is the other way around.

**Narrowing when a code is emitted is not changing what it means.** `self_addressed` now answers
fewer calls than it did. Accepting what used to be refused is the safe direction; emitting an
existing code in a case it did not cover before is the breaking half, and the rule now separates
them.

## What this increment closed

Three backlog entries: `invalid_refs` promising a validation nothing performs, `note` to oneself
being allowed where `ask` was not, and — not previously recorded — the MCP surface swallowing every
refusal.

Six rule clauses were made false and rewritten rather than left standing, across five files:

* [api-guidelines.project.md](../../.claude/rules/api-guidelines.project.md) — "never surfaces a
  raw exception — a model reads the text", asserted since increment 04 and never true. **A rule
  that states a guarantee nothing verifies is how a defect survives five increments.** The rewrite
  names where the guarantee now lives, what a test of it has to assert, and that it covers
  `ChannelException` and nothing wider.
* [concurrency.project.md](../../.claude/rules/concurrency.project.md) — the factor of two in
  `MaxRequestBodySize` is no longer only the JSON around the body, and the 512 KB is now published
  contract, so lowering it is breaking.
* [protocol.project.md](../../.claude/rules/protocol.project.md) — "a difference in behaviour
  between two surfaces is a bug" needed the boundary this increment relies on: a difference that
  comes from how a surface *carries* a field is not one, and the price of that exception is that
  a code reachable on one surface says so in `PROTOCOL.md`.
* [architecture.project.md](../../.claude/rules/architecture.project.md) — "that is the whole
  list" of what a surface may decide. `ArcTools.ParseRefs` is now a permanent addition to it,
  admitted as binding rather than as a rule, with the boundary that nothing else may join it.
* [CLAUDE.md](../../CLAUDE.md) still said 107 tests, and
  [AGENTS.md](../AGENTS.md) claimed to be "deliberately the same advice as the handshake's" after
  the handshake gained a paragraph it did not. The CLI agent is the one that never sees the
  handshake, so that copy is the only one it reads.

Two defects came out of the same review rather than out of a suite: the observer panel dropped
`refs` of `0`, `false` or `""` — legal values only since this increment — behind an `if
(message.refs)`, and four REST refusals this increment introduced were asserted only in
`smoke.sh`, outside the gate. Both are fixed here; `testing.project.md` already said a check that
could live in either belongs in xunit.

## What it left open

`MessageStatus.Expired` is still published and still never produced; removing it is breaking and
waits for a `/v2`. The three delivery findings are untouched and are now the oldest group open.

And one thing this increment added to the backlog: **a unit test failed once, under
`test-all.sh`, and has not failed in the twenty-odd runs since.** The suite piped `dotnet test`
through `tail -3`, which keeps the count and drops the name, so what the failure was is
unrecoverable. That pipe is gone — the suite now prints what failed and keeps the log — which
makes the next single occurrence evidence instead of a memory. Reporting this as "the suite is
green" would have been true and useless.
