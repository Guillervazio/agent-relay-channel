# Increment 07 — a message that is not yours

`GET /v1/messages/{id}` and `GET /v1/threads/{id}` reached `MessageStore` directly and authorised
nobody: any holder of the token that knew an id read the body, and every thread listing hands the
ids out. It was the oldest open finding in [backlog.md](../backlog.md), recorded at the close of
increment 02, and it had a trigger written down — *before the channel carries anything one agent
must not read*.

Nothing about the channel's model changed. One shared token, an agent name that is attribution and
never authorisation ([P004](../adr/P004-one-token-and-an-agent-header.md)), and a panel that reads
every conversation by design ([P010](../adr/P010-the-observer-page-is-unauthenticated.md)) all
still hold. What closed is narrower and was worth closing on its own: reading somebody else's
message was the one case the channel did not notice **at all**, so an honest mistake could not be
told from an intrusion, and the routes that leaked were the ones an honest agent uses.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | The two reads become channel operations, and answer 404 to a stranger | done | `4217270` |
| 2 | The suites drive the refusal against a running hub, on all three surfaces | done | `ff93208` |

Verified at close (5 September 2026): `dotnet build` **0 warnings, 0 errors**; `dotnet test`
**107 passed, 0 failed, 0 skipped**; `dotnet format --verify-no-changes` clean; `dotnet restore
--force` clean, no advisory; `bash scripts/test-all.sh` **four suites green, 76 checks** (29 REST,
18 CLI, 18 MCP, 11 UI).

---

## What the plan got wrong

The plan held better than usual, and the reason is worth naming: the three questions that decide
this change — 403 or 404, the whole thread or the caller's rows, and whether `/v1/observe` was in
scope — were settled **before** the plan was written rather than in it. A plan written first would
have had to guess all three.

| The plan said | What was true |
|---|---|
| The two surfaces would call `ChannelService` "instead of reaching into `MessageStore`" | A swap on the REST side; not on the MCP side. `ArcTools.ThreadAsync` took only a `ChannelService` and had no idea who was calling it — the tool was not merely unauthorised, it was **un-authorisable** without changing its signature. Adding `IHttpContextAccessor` is what made the MCP surface able to hold the rule at all |
| The identical detail text was a caveat about the 404 | It is load-bearing enough to be part of the decision and asserted three times — the whole REST body, the whole CLI output, and the MCP tool's prose. A refusal leaks through its wording as readily as through its status code, and the MCP surface answers **only** in wording |

## What was decided

**[P016](../adr/P016-a-message-is-read-by-its-two-ends.md)** — a message is read by its sender and
its recipient, a stranger gets the same 404 as a nonexistent id, and a thread is trimmed to the
caller's own rows rather than granted to whoever appears in it.

The 404 does not reopen [P006](../adr/P006-403-on-another-agents-mailbox.md)'s 403 on the mailbox.
Both are [H011](../adr/house/H011-404-not-403-when-authorisation-filters-rows.md) applied to two
different facts: `/v1/agents` publishes every agent name, so a 404 there conceals nothing; a
message id is published nowhere, so a 403 there confirms something the caller was not entitled to
know.

The **trimming is what makes the rest hold**. Any agent can join a thread by sending one note with
its `thread_id`, so participation is self-granted — had it been the test, the refusal on
`/v1/messages/{id}` would have been one note away from undone.

**A new refusal on a published route is breaking, with one narrow exception.**
[protocol.project.md](../../.claude/rules/protocol.project.md) had no clause for it although it
had one for narrowing `AgentNamePattern`, which is the same shape. The exception is a refusal no
honest client could hit — the old behaviour being the defect rather than the contract — and the
test is naming the client that breaks and showing it could only be an abusive one. This increment
qualifies; forbidding `note` to oneself, the backlog's neighbouring question, explicitly does not.

**The exception in [architecture.project.md](../../.claude/rules/architecture.project.md) shrank
to what it was always meant to be.** Its list of direct `MessageStore` reads had two entries that
took a decision, and the rule said so and left them there. The test is now written down: a direct
read is a projection only if it would answer **the same thing to every caller**.

## What the close left false, and how

The reconciliation ran before the close and still shipped four contradictions into `master`. They
were found afterwards by the `rules-reviewer` subagent, and the pattern in all four is the same
mistake: **the rules were searched for the claims expected to be false, not read for the ones that
newly govern the code.**

| What was left false | Why the search missed it |
|---|---|
| `architecture.project.md`: *a channel operation ships on all three surfaces*. `MessageAsync` ships on REST alone | The rule was not made false by an edit — it started **applying**. `GET /v1/messages/{id}` had been exempt as a projection, and taking away the exemption is what handed it to a rule nobody had reason to grep for |
| `P011`: *after that, the rule applies without exception* — and increment 07 wrote an exception | The search was for claims about authorisation and projections. This one is about breaking changes, in a record about 422 versus 400, and nothing in it names a route |
| `shared/coding-conventions.md`: *do not use an exception for the query case*. Both new reads throw | It is in a **base**, and the search covered `.claude/rules/*.project.md` and the decision records. The base is where a clause governs without ever having been written for this project |
| `README.md` and `ArcTools`' `[Description]` both still promised the whole conversation | `PROTOCOL.md` was updated in the same commit, as its rule demands, and updating the specified copy felt like updating the copies. The tool's description is the worse of the two: P016 makes the MCP surface's wording load-bearing, and that is the wording |

Fixed in `docs/what-the-close-left-false`, with the base clause taking a `## Deviations` entry
rather than a quiet exception, and the operation-on-one-surface taking the *say why not* the rule
already offered.

## What this closed, and what it did not

Closed: the finding. Removed from [backlog.md](../backlog.md), which is why it is quoted here.

**Not closed, and now recorded as a limitation rather than a gap:** `/v1/observe/history` serves
every message with its body to any holder of the token and does not ask for an agent name at all.
Closing the two routes while that stood would have been closing a door beside an open window, so
the increment says which it is. `smoke.sh` and `HubEndpointTests` both assert the observer still
sees the whole channel — a suite that would not notice that disappearing is worse than no suite,
and this is the second increment in a row where the thing worth testing was the deliberate
behaviour rather than the fix.

The day the token is held by somebody who must not read every conversation, the answer is not a
scoped observer. It is per-agent credentials, and P004 is the record that changes.
