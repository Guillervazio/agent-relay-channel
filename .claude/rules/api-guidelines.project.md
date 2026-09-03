---
paths:
  - "src/Arc.Hub/**/*.cs"
  - "src/Arc.Core/Models.cs"
  - "src/Arc.Core/ChannelService.cs"
---

# API guidelines — this project

Appendix to [shared/api-guidelines.md](shared/api-guidelines.md). The base was written for an MVC
resource server with an envelope and a validation library; ARC is minimal APIs plus MCP over one
service. Its principles hold. Several of its mechanisms have no subject here, and those are
listed rather than quietly dropped.

The wire itself is specified in [docs/PROTOCOL.md](../../docs/PROTOCOL.md), which is the published
contract. This file says how to keep it true; it does not restate it.

## The version is a literal path segment

`/v1/…`, no versioning library, no header negotiation. A breaking change takes a new `/v2` prefix
and new handlers; the old ones freeze. What counts as breaking is in
[protocol.project.md](protocol.project.md), because it is a property of the wire and not of the
HTTP layer.

`/healthz` sits outside the version deliberately. A supervisor probes a fixed path and cannot be
told about a new one.

## Status codes

| Situation | Status |
|---|---|
| Created a request, a response or a note | 201 |
| Read something that exists | 200 |
| A long poll that timed out with nothing to deliver | 200, with an `outcome` saying so |
| Body unreadable — malformed JSON, wrong shape | 400 |
| Well formed, refused by a rule (unknown agent name, body too large, `wait` out of range) | 422 |
| Missing or wrong `X-ARC-Token` | 401 |
| Reading another agent's mailbox | 403 |
| No such message, request or thread | 404 |
| Responding to a request that already has a response | 409 |

422-versus-400 is [H013](../../docs/adr/house/H013-422-for-a-well-formed-request-that-fails-validation.md).
**ARC does not satisfy it today.** Counted rather than estimated, four codes answer 400 that this
table says answer 422, and a fifth is a judgement call:

| Code | Where | Verdict |
|---|---|---|
| `self_addressed` | `ChannelService` | 422. Well formed, refused by a rule |
| `empty_body` | `ChannelService` | 422 |
| `body_too_large` | `ChannelService` | 422 |
| `bad_recipient` | `ChannelService.ValidateAgent` | 422 |
| `bad_agent` | `Arc.Hub/Program.cs`, the `X-ARC-Agent` header | **Undecided.** A malformed header is arguably a request that could not be read, which is the 400 case. Decide it in the commit that moves the other four |
| `invalid_json` | `Arc.Hub/Program.cs` | 400, and correct. The body could not be read at all |

The table above is the target; `docs/backlog.md` carries the gap. It is written as a gap and not
as fact because a rule describing the code as one wishes it were is a rule nobody can check.

## 403, not 404, on another agent's mailbox

Against [H011](../../docs/adr/house/H011-404-not-403-when-authorisation-filters-rows.md), for a
reason that is a fact about this code rather than a preference — see
[P006](../../docs/adr/P006-403-on-another-agents-mailbox.md). `/v1/agents` publishes every agent
id to every authenticated caller, so a 404 would conceal nothing, while lying to an agent who
mistyped their own name.

## Error codes are a published set

One definition, and a test spells the literal rather than referencing the constant —
[H012](../../docs/adr/house/H012-an-error-code-is-defined-once-and-a-test-keeps-the-literal.md).
ARC violates both halves today: the codes exist in four copies (`ChannelService`,
`Arc.Hub/Program.cs`, `ArcTools`, and the table in `docs/PROTOCOL.md`) and no test asserts any of
them. The table in `PROTOCOL.md` stays the published copy; the other three collapse into one.

## Identity is a header, and it is not a credential

`X-ARC-Token` authenticates. `X-ARC-Agent` says who is speaking, and any authenticated caller can
present any name — [P004](../../docs/adr/P004-one-token-and-an-agent-header.md). So the agent name
is **attribution, never authorisation**, and no decision that matters may rest on it alone.
`X-ARC-Provider` is metadata for the observer panel and nothing reads it to decide anything.

Anonymous mode (`ARC_ALLOW_ANONYMOUS=1`) binds loopback only. That coupling is the rule: the day
anonymous mode listens on a routable address, it stops being a development convenience.

`/ui` is served unauthenticated on purpose — a page that asks for the token cannot require the
token to load. It follows that **no data may be rendered into that page**; it fetches with the
token the viewer supplies. See [P010](../../docs/adr/P010-the-observer-page-is-unauthenticated.md).

## Long polls

Every endpoint that can wait takes `?wait=` in seconds, bounded by `ARC_MAX_WAIT`.

Today `ChannelService.Clamp` is `Math.Clamp(requested ?? 0, 0, MaxWaitSeconds)`: a caller asking
for 600 against a maximum of 300 is **silently given 300**, and its poll returns at half the time
it asked for with an `outcome` that looks like an ordinary timeout. That is the shape the base
warns about, and it is a gap in `docs/backlog.md` rather than a rule pretending to be satisfied.

The rule, for anything written from here on: an out-of-range `wait` is **422**. The caller asked
for something the server will not do, and answering as though it agreed makes a truncated wait
indistinguishable from a real one.

A timed-out poll is a **200 with an `outcome`**, not a 404 and not a 408: nothing failed, and the
request it was waiting on is still alive in the mailbox. Every long-lived handler honours
`HttpContext.RequestAborted`; the mechanics are in
[concurrency.project.md](concurrency.project.md).

## The MCP surface adds nothing REST lacks

Seven tools over the same `ChannelService`. A tool never reaches past it, never adds an operation
the REST surface does not have, and never surfaces a raw exception — a model reads the text.

A tool's `Description` is written for a model, not a person: it says when to call the tool, not
what the tool is. Every tool's output names the next action, because a model that has just been
told "no hay mensajes" needs to know whether to wait again.

## What the base asks for that has no subject here

Recorded so the next reader does not go looking:

* **The response envelope** (`data` / `meta` / `error`). ARC returns the resource directly, with
  errors as `{ error: { code, message } }`. `PROTOCOL.md` is the description.
* **FluentValidation.** Validation is hand-written in `ChannelService`, against its parameters —
  which is [H005](../../docs/adr/house/H005-validation-belongs-to-the-command.md) satisfied, not
  avoided: it applies whether the call arrives over REST, MCP or the CLI.
* **Controllers, `[ProducesResponseType]`, model binding.** Minimal APIs, no MVC.
* **Paging.** No listing is unbounded, but the limits are fixed server-side rather than
  parameters: the inbox is one agent's, and `ListThreadsAsync` takes a `limit` defaulting to 200.
  There is no `sortBy`, so the base's allow-list clause has nothing to guard.
* **Named authorisation policies.** There is one token and one rule; a policy framework over that
  would be ceremony.

## Deviations

None. Everything above is either a base clause satisfied differently or a clause with no subject,
and the two gaps — the 400s that should be 422s, and the four copies of the error codes — are
recorded as work owed rather than as departures.
