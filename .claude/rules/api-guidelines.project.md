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
| Created a note, or answered a request | 200 with the message |
| A request answered inside its `wait` | 200 with the answer |
| A request that was queued or whose `wait` ran out | 202, `Location` at the message, `outcome` saying which |
| Read something that exists | 200 |
| Body unreadable — malformed JSON, wrong shape | 400 |
| Well formed, refused by a rule (unknown agent name, body too large, `wait` out of range) | 422 |
| Missing or wrong `X-ARC-Token` | 401 |
| Reading another agent's mailbox | 403 |
| No such message, request or thread — or one you are not a party to | 404 |
| Responding to a request that already has a response | 409 |

422-versus-400 is [H013](../../docs/adr/house/H013-422-for-a-well-formed-request-that-fails-validation.md),
adopted as [P011](../../docs/adr/P011-422-for-a-refused-request.md) and **satisfied**: exactly one
code answers 400, and it is the one whose request could not be read.

| Code | Status | Why that one |
|---|---|---|
| `invalid_json` | 400 | The body could not be parsed at all. This is the whole 400 case |
| `bad_agent`, `bad_recipient`, `empty_body`, `body_too_large`, `invalid_refs`, `invalid_wait`, `self_addressed` | 422 | Read successfully, refused by a rule |

`bad_agent` was the judgement call and went to 422: it is `AgentNamePattern` refusing a value, and
the same pattern refusing the same shape in the body is `bad_recipient`. One validator answering
two different statuses depending on where the value travelled is the incoherence H013 removes.

## 403 on a mailbox, 404 on a message

Both are [H011](../../docs/adr/house/H011-404-not-403-when-authorisation-filters-rows.md) applied
to two different facts, and the fact is what decides — not the shape of the route.

The mailbox answers **403**, against H011's default, because `/v1/agents` publishes every agent id
to every authenticated caller: a 404 would conceal nothing while lying to an agent who mistyped
their own name — [P006](../../docs/adr/P006-403-on-another-agents-mailbox.md).

`GET /v1/messages/{id}` and `GET /v1/threads/{id}` answer **404**, with H011, because a message id
is published nowhere: a 403 would confirm that it exists. A thread is trimmed to the caller's own
rows first, so appearing in a thread — which any agent can arrange by sending one note into it —
is not what grants the history. [P016](../../docs/adr/P016-a-message-is-read-by-its-two-ends.md).

**The identical body is part of the rule, not a detail of it.** A 404 whose `detail` differs
between "no such id" and "not yours" says in prose exactly what the status code was chosen not to
say, and so does an MCP tool that words the two cases differently. The tests compare the whole
body for that reason.

What this does not authorise: reading either one as a preference to copy. A new route asks which
fact holds for it — whether existence is already public — and answers accordingly. And none of it
makes the channel confidential; see the *Identity is a header* section below, which is unchanged.

## Error codes are a published set

One definition, and a test spells the literal rather than referencing the constant —
[H012](../../docs/adr/house/H012-an-error-code-is-defined-once-and-a-test-keeps-the-literal.md).
ARC satisfies both halves. `Arc.Core/ArcErrors.cs` is the one definition; `ChannelService`,
`Arc.Hub/HubApp.cs` and `ArcTools` reference it. `ArcErrorsTests` freezes all twelve literals,
and a second test reflects over `ArcErrors` and fails if one is missing from the table in
`docs/PROTOCOL.md`, which stays the published copy.

That second test is deliberately the other shape: it **discovers** the value instead of freezing
it, because its job is to catch the two copies diverging — a hand-written list would pass exactly
when somebody adds the thirteenth code and edits neither side, which is how `invalid_refs` once
went unpublished.

What this does not authorise: a magic string in a test generally. The literal is spelled here
because the test *is* the client of a published code.

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

An out-of-range `wait` is **422 (`invalid_wait`), never a silent clamp.** The caller asked for
something the server will not do, and answering as though it agreed makes a truncated wait
indistinguishable from a real timeout — the poll comes back early with an `outcome` that says
nothing happened, which is true and useless.

`ChannelService.ValidateWait` is the one place that decides it, and every caller validates
**before** doing any work: a rejected `wait` must not leave a request created in the channel and
then answer 422 to the agent that created it.

**This is a rule about bounds a caller names, not about waiting.** `?replay=` is the second one
(`invalid_replay`, `ChannelService.ValidateReplay`, next to `ValidateWait`), and it is refused
rather than narrowed for the same reason: a request quietly cut down comes back
indistinguishable from an honest empty answer, so the caller never finds out it was overruled. A
third such parameter is refused the same way, and gets its own code — one shared code cannot say
which of two bounds was the problem.
<br>**What this does not authorise: refusing a value the caller can sensibly mean.** `wait=0` is
"do not wait" and `replay=0` is "do not look back"; both are accepted. Only what falls outside
the published range is refused.

A timed-out poll is a **200 with an `outcome`**, not a 404 and not a 408: nothing failed, and the
request it was waiting on is still alive in the mailbox. Every long-lived handler honours
`HttpContext.RequestAborted`; the mechanics are in
[concurrency.project.md](concurrency.project.md).

## The MCP surface adds no operation REST lacks

Seven tools over the same `ChannelService`. A tool never reaches past it, never adds an operation
the REST surface does not have, and never surfaces a raw exception — a model reads the text.

**That last clause was false for five increments, and asserting it is what kept it invisible.** The
SDK turns any exception a tool throws into `An error occurred invoking 'arc_x'`, so every refusal
the channel makes — `invalid_refs`, `self_addressed`, `bad_recipient`, `not_found` — reached the
model as the same English sentence with no code and no reason. The guarantee now lives in a
`CallToolFilter` registered in `HubApp`, which translates `ChannelException` into
`«code»: «detail»` once for all seven tools
([P019](../../docs/adr/P019-a-refusal-a-model-cannot-read-is-not-a-refusal.md)).

Two consequences worth keeping:

* **A tool must not catch `ChannelException` itself.** The filter is the one translation, for the
  same reason `ChannelService` is the one rule: seven copies drift. `ArcTools.ThreadAsync` returning
  prose rather than throwing is the single exception, and it is not about errors — saying *whether
  a thread exists* is what its 404 is chosen not to say
  ([P016](../../docs/adr/P016-a-message-is-read-by-its-two-ends.md)).
* **A test that asserts a refusal over MCP asserts the absence of that sentence too.** Checking only
  that the call failed passes in exactly the state this fixed.

**What this does not authorise: widening the filter past `ChannelException`.** Everything else is
still an unhandled fault and still becomes the SDK's sentence, and that is correct — an unexpected
exception's message is not written for a model and may carry detail a caller has no business
reading. So the clause above is exact rather than absolute: a tool never surfaces a raw
*`ChannelException`*.

It does carry one thing REST has no equivalent for, and it is not an operation: the `initialize`
handshake returns `ServerInstructions`, which is how the channel explains itself to the model at
the other end ([P014](../../docs/adr/P014-the-channel-explains-itself-in-the-handshake.md)).

Four kinds of prose, and each says only its own thing:

* A tool's **`Description`** says *when to call that tool*, not what the tool is.
* A tool's **output** names the next action, because a model that has just been told
  "no hay mensajes" needs to know whether to wait again.
* **`ServerInstructions`** says how the channel is used at all — and nothing a single tool
  already says about itself, which is what the SDK's own guidance asks for.
* A **refusal** leads with the error code and then the same `detail` REST returns. The code leads
  because the code is what does not change meaning; MCP has no status line to carry it instead.

All four are written for a model, not for a person. None of them is a place for a rule the
channel enforces: that is `ChannelService`, reached by all three surfaces.

## What the base asks for that has no subject here

Recorded so the next reader does not go looking:

* **The response envelope** (`data` / `meta` / `error`). ARC returns the resource directly, with
  errors **flat**: `{"error": "<code>", "detail": "<explanation>"}`, which is `ErrorBody` in
  `Models.cs` and the table in `PROTOCOL.md`. Not the base's nested `error.code` / `error.message`.
* **FluentValidation.** Validation is hand-written in `ChannelService`, against its parameters —
  which is [H005](../../docs/adr/house/H005-validation-belongs-to-the-command.md) satisfied, not
  avoided: it applies whether the call arrives over REST, MCP or the CLI.
* **Controllers, `[ProducesResponseType]`, model binding.** Minimal APIs, no MVC.
* **Paging.** No listing is paged: nothing takes an offset or a cursor, and no caller can ask
  for page two. `ListThreadsAsync` takes a `limit` defaulting to 200. The inbox returns what is
  pending plus whatever `?unanswered=` and `?replay=` ask back, and `replay` is a bound the
  **caller** supplies — which this clause used to deny, on the reasoning that being one agent's
  mailbox was bound enough. It is not: a request leaves the recovery set by being answered and a
  note never does, so a recovery that reaches notices needs a bound from somewhere, and the
  caller is the only party that knows how far back to look
  ([P020](../../docs/adr/P020-a-recovery-window-not-a-state.md)). There is no `sortBy`, so the
  base's allow-list clause has nothing to guard.
  <br>**What this does not authorise: a `limit`, an `offset` or a cursor on the inbox.** `replay`
  decides *which* messages are in the answer, not how much of a fixed answer you are given —
  every message inside the window comes back on every call, and that is exactly what makes
  re-reading repeatable.
* **Named authorisation policies.** There is one token and one rule; a policy framework over that
  would be ceremony.

## Deviations

### A create answers 200 or 202, never 201

Replaces the base clause under *Which status code*: "**201** for a create, **with a `Location`
header pointing at what was created**."

`POST /v1/notes` and `POST /v1/requests/{id}/response` answer **200 with the message itself**, and
`POST /v1/requests` answers **200** when the reply arrived inside the `wait` or **202** when it did
not. The 202 carries the `Location`.

The reason is that `POST /v1/requests` is not really a create — it is a **call**. A client posting
it is not asking for a row, it is asking a question and blocking on the answer, and the answer is
in the body. 201 would describe the byproduct and hide the result. Having settled that for the
blocking case, 200 for the other two keeps one shape across the surface rather than two.

This is now also **published** in `PROTOCOL.md`, which makes changing it breaking: a different
status for an existing route is a `/v2`, per [protocol.project.md](protocol.project.md).

What this does not authorise: dropping `Location` from the 202. A client that timed out has to be
able to find the request it just made without reconstructing the URL.

### Nothing else

Everything else above is either a base clause satisfied differently or a clause with no subject.
