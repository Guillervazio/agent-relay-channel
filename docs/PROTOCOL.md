# ARC protocol

The channel's contract. Three surfaces (REST, MCP and the CLI) over the same logic: `ChannelService`
in [src/Arc.Core/ChannelService.cs](../src/Arc.Core/ChannelService.cs).

## Core idea

A command-line agent **is not a server**: it only exists for the duration of its turn.
It cannot hold an open subscription, nor react to an event that arrives while it is idle.
That is why the channel does not use a message broker, but **HTTP long polling**: the
agent's request stays open on the server until the answer arrives or the deadline runs
out. It fits the turn-based model with no extra infrastructure.

## Identity

Every request (except `/healthz`) carries two headers:

| Header | Mandatory | Contents |
|---|---|---|
| `X-ARC-Agent` | yes | Sender's identity: `^[a-z0-9][a-z0-9._-]{0,63}$` |
| `X-ARC-Token` | if the hub has `ARC_TOKEN` | Shared secret |
| `X-ARC-Provider` | no | Informational label: `claude-code`, `codex`… |

The agent's name is the key of the wait registry, hence the constrained format.
An agent may only **read its own mailbox** and **answer what is addressed to it**.

## Message types

| Type | Expects an answer | Use |
|---|---|---|
| `request` | yes | Ask something you need in order to continue |
| `response` | — | Reply to a `request`, tied to it by `correlation_id` |
| `note` | no | Announce an accomplished fact |

### States

`pending` → `delivered` (when read from the mailbox) → `answered` (only `request`, when replied to).

A `request` that was delivered but not answered is still recoverable with
`?unanswered=true`: that is the recovery path if an agent dies before replying.

A `note` is not covered by that, and cannot be: it is never *unanswered*, because it is never
answered. It has **no terminal state** — it stays `delivered` for ever, where a `request` leaves
the recovery set by becoming `answered`. So the way back to a delivered notice is a window in
time rather than a state: `?replay=N` returns everything addressed to you in the last `N`
seconds, whatever its kind and whatever its status, alongside whatever is `pending` as usual.

That is the recovery path when a mailbox response is lost in transit — the messages were marked
`delivered` before the response was written, so a response that never arrives takes them out of
the default mailbox. **Re-reading writes nothing**: nothing is marked, no status changes and no
waiting poll is woken, so the same window returns the same messages as many times as it is asked.
`N` is counted back from the hub's own clock, so a caller says *how long ago* and never *since
when* — which is the only one of the two it still knows, having never received the ids or the
timestamps in the response it lost. `N` runs from 0 (look back at nothing) to 86400, and one
outside that is **refused with `422 invalid_replay`, never silently clamped**, for the same
reason `wait` is: a narrowed window returns fewer messages and the caller cannot tell.

## REST endpoints

| Method | Path | Behaviour |
|---|---|---|
| `POST` | `/v1/requests?wait=N` | Creates the request. Blocks until the answer: `200` with it, `202` with `outcome: timeout` when the deadline passes. |
| `GET` | `/v1/requests/{id}/response?wait=N` | Resumes the wait on a request that already expired. |
| `POST` | `/v1/requests/{id}/response` | Answers. Wakes the sender instantly. |
| `POST` | `/v1/notes` | Notice with no answer expected. |
| `GET` | `/v1/inbox/{agent}?wait=N&unanswered=&replay=N` | Your own mailbox. `204` if nothing arrives within the deadline. |
| `GET` | `/v1/threads/{id}` | The conversation, in order — your messages in it. `404` if none are. |
| `GET` | `/v1/messages/{id}` | One specific message, if you sent it or it was sent to you. `404` otherwise. |
| `GET` | `/v1/agents` | Agents seen. |
| `GET` | `/healthz` | State, active waits and agents. Unauthenticated. |
| `GET` | `/ui` | Observation panel. Unauthenticated: it is a page with no data inside. |
| `GET` | `/v1/observe/history?limit=N&thread=` | The tail of the history plus agents and waits. With `thread`, only that conversation. |
| `GET` | `/v1/observe/threads?limit=N` | Index of conversations, most recent first. |
| `GET` | `/v1/observe/stream` | SSE stream of what is happening. |

`wait` is in seconds and must not exceed `ARC_MAX_WAIT` (300 by default). One that does is
**refused with `422 invalid_wait`, never silently clamped** — a shortened poll comes back with
an `outcome` indistinguishable from a real timeout, and the caller never finds out. The refusal
happens before anything is created, so a rejected `wait` never leaves a request in the channel.
`wait=0` queues and returns.

### What you may read

`/v1/messages/{id}` and `/v1/threads/{id}` answer only for the two ends of the message. A message
you neither sent nor received, and one that does not exist, get **the same `404` with the same
body** — a `403` would confirm that the id exists, which is what the `404` is there not to say. A
thread is trimmed to your own messages in it rather than served whole to anyone appearing in it:
sending one note into a thread is enough to appear in it, so appearing cannot be what grants the
history.

This is not confidentiality, and it is not a substitute for it. One shared token, an agent name
that is attribution and never authorisation, and `/v1/observe` reading the whole channel by design
all still hold — see *What the shared token does not protect* in the README. What these two routes
stop is the case where reading someone else's message was not even a mistake the channel noticed.

### Observation

The `/v1/observe` routes ask for `X-ARC-Token` like any other, but **not**
`X-ARC-Agent`: whoever is looking does not take part, so it has no identity on the
channel, does not show up in `/v1/agents` and does not change the state of any message.
Reading the whole channel is exactly what tells them apart from the mailbox, which only
shows your own.

`/v1/observe/threads` is the index you pick from: one row per thread, and no bodies.

```json
{
  "thread_id": "thr_1d865fd649404bdd",
  "subject": "Unit of the `total` field in the payment endpoint",
  "participants": ["claude-pc1", "codex-pc2"],
  "messages": 2,
  "open_requests": 0,
  "closed": true,
  "started_at": "2026-09-03T20:16:32.633+00:00",
  "last_at": "2026-09-03T20:17:32.458+00:00"
}
```

`closed` is not stored anywhere: it is derived from there being no unanswered question
left in the thread. That is why a thread made only of notices is born finished — nobody
is going to answer a notice — and why a conversation can reopen if someone asks again
inside it. The index does not travel over the stream: whoever is watching asks for it
again when the stream announces a new message.

`/v1/observe/stream` is Server-Sent Events. Four kinds of event:

| Event | When | Contents |
|---|---|---|
| `hello` | Once, when the stream opens | `{ "max_wait_seconds": N, "database": "…", "server_time": "…" }` |
| `message` | A `request`, `response` or `note` is created | `{ "event": "message", "message": { … } }` |
| `delivered` | An agent reads its mailbox | `{ "event": "delivered", "ids": ["req_…"] }` |
| `state` | The waits or the agents change | `{ "waiters": { … }, "agents": [ … ], "observers": N }` |

Each `data:` is a single line: a newline inside it would split the event in two. When
there is no traffic, the hub sends a `: ping` comment every two seconds so the
connection is not closed. A slow observer never holds up the channel: its queue is
bounded and drops the oldest.

### Body of a request

```json
{
  "to": "codex-pc2",
  "subject": "Contract of the payments endpoint",
  "body": "Does the total field travel in cents?",
  "refs": { "branch": "feat/payments", "commit": "a1b2c3d", "files": ["src/payments/Total.cs"] },
  "thread_id": "thr_1a2b3c"
}
```

`refs` is **any JSON value**, stored and returned verbatim. An object is the convention and
what every example here shows, but nothing rejects an array, a string or a number, and no
client should be written expecting that they are refused.

**Send references, not content**: both machines have a clone of the same repository, so a
commit and a path are enough. The body is limited to 256 KB and the hub rejects anything
beyond that; `refs` has no separate limit, so what caps it is the request as a whole, which
Kestrel holds to 512 KB.

### Result of a request

```json
{
  "outcome": "answered",
  "request_id": "req_1a2b3c",
  "thread_id": "thr_4d5e6f",
  "response": { "id": "res_...", "from": "codex-pc2", "body": "...", "kind": "response" }
}
```

`outcome` is `answered`, `timeout` or `queued` (when `wait=0` was asked for).

### Errors

Always `{"error": "<code>", "detail": "<explanation>"}`:

| Code | HTTP | Reason |
|---|---|---|
| `unauthorized` | 401 | `X-ARC-Token` missing or wrong |
| `invalid_json` | 400 | The body could not be read: not valid JSON, or it did not arrive as UTF-8 |
| `bad_agent` | 422 | `X-ARC-Agent` missing or malformed |
| `bad_recipient` | 422 | `to` missing or malformed |
| `empty_body` | 422 | The body is missing |
| `body_too_large` | 422 | More than 256 KB |
| `invalid_refs` | 422 | `refs` could not be read as JSON. **MCP only** — see below |
| `invalid_wait` | 422 | `wait` outside the range the hub accepts |
| `invalid_replay` | 422 | `replay` outside the range the hub accepts (0 to 86400 seconds) |
| `self_addressed` | 422 | An agent asked to wait on its own answer |
| `forbidden` | 403 | Someone else's mailbox, or answering something not addressed to you |
| `not_found` | 404 | No such request, message or thread — or none you may read |
| `already_answered` | 409 | That request already has an answer |

`invalid_refs` is reachable over MCP alone, and that is the shape of the wire rather than
an omission: MCP takes `refs` as a string argument the hub parses on its own, so it can fail
by itself. Over REST `refs` travels inside the request body, so malformed `refs` are a
malformed body and answer `invalid_json` 400. The CLI parses `--refs` before it sends
anything and exits 2 without reaching the hub.

`400` is only for what could not be read. A request that arrived intact and that a rule
said no to answers `422`: that way a client tells its own serialisation failure apart
from a rule it has broken, without looking at the code.


## MCP tools

At `/mcp`, Streamable HTTP transport. The same operations, with the output worded so a
model can read it:

| Tool | What it does |
|---|---|
| `arc_ask` | Asks and waits. Blocks until the answer or the deadline. |
| `arc_await` | Resumes the wait on a request that expired. |
| `arc_inbox` | Reads your mailbox; with `wait` it stays waiting, with `replay` it looks back. |
| `arc_respond` | Answers a request addressed to you. |
| `arc_note` | Announces without waiting for an answer. |
| `arc_thread` | Retrieves a conversation — your messages in it. |
| `arc_agents` | Lists who is on the channel. |

### What a refusal looks like

A tool that the channel refuses answers `isError: true` with one line of text:

```
self_addressed: Un agente no puede esperar su propia respuesta. Con 'wait' a 0 la peticion queda en tu buzon.
```

The code is the same one REST puts in `error`, and it leads. The sentence after it is the
same `detail`, and like every other detail it is prose that may be reworded — key on the
code, never on the wording. MCP has no status codes, so this line is the whole of what the
caller gets; without the code in it, a model would learn only that something failed.

`arc_thread` is the exception, and deliberately: it answers a thread that is not yours in
ordinary prose rather than as an error, because saying *whether it exists* is what
[the 404](#what-you-may-read) is chosen not to say.

### The handshake carries the channel's instructions

`initialize` answers with an `instructions` field: natural-language guidance for the model on how
to use the channel — check the mailbox at the start of a turn, ask versus notify, send references
rather than content, and never both wait at once. It names no project, no machine and no agent: the
recipient is discovered with `arc_agents`.

It is there so a consuming repository has nothing to write down. Pasting those rules into each
project's own instructions means one copy per repository, and copies drift.

Whether a client puts that text in front of its model is the client's decision, not this
contract's. What the hub guarantees is that the field arrives, and is not empty.

## The CLI

`arc` is the third surface. It carries the same operations over REST, and adds one thing the
other two do not have: **an exit code a shell script can branch on**. The codes are contract —
one never changes meaning, and a new state takes a new number.

| Code | Meaning |
|---|---|
| `0` | Answered, or there are messages, or the operation succeeded |
| `1` | Network or hub error |
| `2` | Incorrect use of the command |
| `3` | The wait expired with no answer, or `--wait 0` queued the request |
| `4` | The mailbox is empty |

`3` and `4` are not failures. `3` means the request is still alive in the recipient's mailbox
and can be resumed with `arc await`; `4` means there was nothing to deliver.

`arc inbox --replay N` is the third surface's form of the recovery window, and it forwards `N`
to the hub exactly as given rather than judging it, so an out-of-range value comes back as
`invalid_replay` from the one place that decides it. Left out, nothing is looked back at.

## Encoding

Everything is UTF-8. **On Windows, do not pass bodies with accented characters through
the command line**: argv crosses the ANSI codepage and corrupts them before curl sends
them. Use `arc ... --body-file file.md`, or `--data-binary @file` with curl. The hub
rejects those bodies with `invalid_json` instead of storing broken text.

## Writing to yourself

An agent may address a `note` or a `request` to itself, and the request lands in its own
mailbox like any other. What it may not do is **wait** on it: `wait > 0` on a request whose
sender and recipient are the same answers `self_addressed` 422, because the only party that
could answer is the one blocked waiting, so the wait can only end in a timeout. `wait = 0`
returns `queued` and the request is there on the next turn — which is the point, since an
agent that only exists during its turn has reason to leave work for the next one.

The refusal is on the wait and not on the message, so it applies wherever a wait is asked
for: `POST /v1/requests?wait=N`, and `GET /v1/requests/{id}/response?wait=N` on a request
you sent yourself. Collecting an answer that already exists is never refused, including one
you gave yourself in an earlier turn.

## Deadlock

Two agents waiting for each other burn through their turns without progressing.
Mitigations:

- Every `ask` carries a deadline; when it passes, the request stays alive and the work
  goes on.
- `/healthz` exposes `waiters`, where a mutual wait is visible at a glance.
- The one-agent case of this — waiting on your own request — is refused outright rather than
  mitigated, because nothing could resolve it. See [Writing to yourself](#writing-to-yourself).
