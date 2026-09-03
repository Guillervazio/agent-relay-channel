---
paths:
  - "src/Arc.Core/WaiterRegistry.cs"
  - "src/Arc.Core/EventStream.cs"
  - "src/Arc.Hub/Program.cs"
---

# Concurrency and long polling — this project

**This area has no shared base.** It is what ARC is: a blocking request/response between two
agents, built out of long polls. `dotnet-house` has nothing on it because the project it came
from has nothing that waits.

## Register the waiter before the read that could miss the signal

The invariant of the whole design. A caller that wants to be woken registers **first**, then
reads the current state; if the signal arrives in between, the registration catches it.

Read the other way round — check first, register second — and a message that lands in that window
is lost, and the caller waits its full timeout for something that already happened. This is what
`Una_senal_previa_a_la_espera_no_se_pierde` exists to hold.

It appears in `ChannelService.AskAsync`, `AwaitResponseAsync` and `InboxAsync`. It was a comment
in three places and a rule in none, which is why it is written here.

## A waiter is single-use

`Register` hands back a `Waiter` that completes at most once. Disposing it always completes it —
a disposed waiter that leaves its `TaskCompletionSource` unresolved is a request thread parked
forever.

`TaskCreationOptions.RunContinuationsAsynchronously` is **mandatory** on every
`TaskCompletionSource` here. Without it, the continuation resumes on the thread that called
`TrySetResult` — which is the thread serving the *writer's* request. One agent's `respond` would
then run the other agent's response rendering before its own reply is sent.

## Cancellation is a normal ending

Every long-lived handler honours `HttpContext.RequestAborted`. An `OperationCanceledException` at
the boundary of a long poll means the client hung up or the timeout elapsed: it is an ordinary
outcome, not a failure, and it is neither logged as an error nor turned into a 500.

This is the one place `shared/coding-conventions.md`'s "never swallow an exception" is answered
with a catch that does nothing. It is named here so the rule and the code do not contradict each
other. The licence is exactly this: `OperationCanceledException`, at the boundary of a wait whose
token was cancelled. Anywhere else it is the base's rule, unchanged.

## An observer never slows the channel

`EventStream` is a bounded channel with `DropOldest`. The publisher never blocks and never waits
for a subscriber: an observer page on a slow link loses events, and that is the correct trade —
the panel is a view, and a channel that stalls because someone left a browser tab open is a
channel that has made its worst client its bottleneck.

`Subscription` is disposed on the way out of the handler, always, so a dropped SSE connection
does not leak a queue.

## Kestrel's tuning is correctness, not tuning

Three settings in `Arc.Hub/Program.cs`, each **derived** from something and never set
independently of it:

| Setting | Derived from | What breaks otherwise |
|---|---|---|
| `KeepAliveTimeout` | `ARC_MAX_WAIT` plus a margin | Kestrel closes the connection under a poll that is still legitimately waiting |
| `MaxRequestBodySize` | `MessageStore.MaxBodyBytes * 2` | The store's own limit becomes unreachable: Kestrel cuts the request off first, with a different error and no `body_too_large` code. The factor of two is the JSON around the body, not slack |
| `MinResponseDataRate = null` | The SSE heartbeat interval | The default minimum rate kills an observer stream that only sends a heartbeat every two seconds |

Change `ARC_MAX_WAIT`'s ceiling or the body limit and the derived setting moves in the same
commit.

**Nothing exercises a wait longer than 60 seconds.** The longest smoke waits 60; the keep-alive
derivation is therefore unverified above that, and it is in `docs/backlog.md` with the trigger
that would settle it.

## Shared state between tests

`WaiterRegistry` is a `ConcurrentDictionary` that lives as long as the process. A test that
registers a waiter and does not dispose it leaves that key populated for whatever runs next in
the same class. The suite has no container to reset, so this is the coupling to watch here.
