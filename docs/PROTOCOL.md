# ARC protocol

The channel's contract. Two surfaces (REST and MCP) over the same logic: `ChannelService`
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

## REST endpoints

| Method | Path | Behaviour |
|---|---|---|
| `POST` | `/v1/requests?wait=N` | Creates the request. Blocks until the answer: `200` with it, `202` with `outcome: timeout` when the deadline passes. |
| `GET` | `/v1/requests/{id}/response?wait=N` | Resumes the wait on a request that already expired. |
| `POST` | `/v1/requests/{id}/response` | Answers. Wakes the sender instantly. |
| `POST` | `/v1/notes` | Notice with no answer expected. |
| `GET` | `/v1/inbox/{agent}?wait=N&unanswered=` | Your own mailbox. `204` if nothing arrives within the deadline. |
| `GET` | `/v1/threads/{id}` | The complete conversation, in order. |
| `GET` | `/v1/messages/{id}` | One specific message. |
| `GET` | `/v1/agents` | Agents seen. |
| `GET` | `/healthz` | State, active waits and agents. Unauthenticated. |
| `GET` | `/ui` | Observation panel. Unauthenticated: it is a page with no data inside. |
| `GET` | `/v1/observe/history?limit=N&thread=` | The tail of the history plus agents and waits. With `thread`, only that conversation. |
| `GET` | `/v1/observe/threads?limit=N` | Index of conversations, most recent first. |
| `GET` | `/v1/observe/stream` | SSE stream of what is happening. |

`wait` is in seconds and is clamped to `ARC_MAX_WAIT` (300 by default). `wait=0` queues and returns.

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

`/v1/observe/stream` is Server-Sent Events. Three kinds of event:

| Event | When | Contents |
|---|---|---|
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

`refs` is a free-form JSON object. **Send references, not content**: both machines have
a clone of the same repository, so a commit and a path are enough. The body is limited
to 256 KB and the hub rejects anything beyond that.

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
| `invalid_refs` | 422 | `refs` is not a valid JSON object |
| `invalid_wait` | 422 | `wait` outside the range the hub accepts |
| `self_addressed` | 422 | An agent writing to itself |
| `forbidden` | 403 | Someone else's mailbox, or answering something not addressed to you |
| `not_found` | 404 | No such request or thread |
| `already_answered` | 409 | That request already has an answer |

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
| `arc_inbox` | Reads your mailbox; with `wait` it stays waiting. |
| `arc_respond` | Answers a request addressed to you. |
| `arc_note` | Announces without waiting for an answer. |
| `arc_thread` | Retrieves a complete conversation. |
| `arc_agents` | Lists who is on the channel. |

## Encoding

Everything is UTF-8. **On Windows, do not pass bodies with accented characters through
the command line**: argv crosses the ANSI codepage and corrupts them before curl sends
them. Use `arc ... --body-file file.md`, or `--data-binary @file` with curl. The hub
rejects those bodies with `invalid_json` instead of storing broken text.

## Deadlock

Two agents waiting for each other burn through their turns without progressing.
Mitigations:

- Every `ask` carries a deadline; when it passes, the request stays alive and the work
  goes on.
- `/healthz` exposes `waiters`, where a mutual wait is visible at a glance.
