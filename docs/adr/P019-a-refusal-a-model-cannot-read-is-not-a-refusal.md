# P019 — A refusal a model cannot read is not a refusal

## Context

Verifying [P017](P017-refs-is-any-json-value.md) and
[P018](P018-an-agent-may-queue-work-for-itself.md) against a running hub turned up something no
suite and no finding had recorded: **the MCP surface delivered no refusal at all**. Every
`ChannelException` came back as

```
{"content":[{"type":"text","text":"An error occurred invoking 'arc_note'."}],"isError":true}
```

— the same sentence for `invalid_refs`, `self_addressed`, `bad_recipient` and `not_found`, in
English, with no code and no reason. The SDK catches the exception and replaces its message.

So `invalid_refs` was worse off than the finding said. Not merely unreachable over REST: over MCP
it was raised and then thrown away, and so was every other code the protocol publishes. And P018's
new refusal was useless to its one natural audience — telling an agent to use `wait = 0` only helps
if the agent can read it, and the agent reading it is a model with no status line to fall back on.

[api-guidelines.project.md](../../.claude/rules/api-guidelines.project.md) asserted the opposite in
so many words: a tool "never surfaces a raw exception — a model reads the text". Asserting it is
part of why it stayed invisible for five increments.

## Decision

**A `CallToolFilter` registered in `HubApp` translates `ChannelException` into one line of text**,
`«code»: «detail»`, with `isError: true`. One registration for all seven tools, rather than seven
`try`/`catch` blocks in `ArcTools`.

**The code leads.** It is the half that does not change meaning; the detail after it is the same
prose REST returns in `detail`, and no client may key on its wording.

**A tool does not catch `ChannelException` itself.** `ArcTools.ThreadAsync` returning prose rather
than throwing stays the single exception, and it is not about error reporting: saying *whether a
thread exists* is what its 404 is chosen not to say
([P016](P016-a-message-is-read-by-its-two-ends.md)).

## Consequences

This is `protocol.project.md`'s "three surfaces, one wire" failing at the seam it names. A
difference in *behaviour* between two surfaces is a bug in whichever one departed from
`ChannelService`, and MCP had departed from all of it — not by answering something different, but
by answering nothing at all.

The one place that had already worked around it went into increment 07 as a decision about the
404. It was also the symptom of this, and nobody noticed, because a workaround that is justified on
other grounds reads as complete.

The SDK's documentation for `CallToolFilters` says those filters wrap the handler invoked for a
tool "that isn't found in the `McpServerTool` collection", which reads as though registered tools
are not covered. They are. Compiling and calling a real hub is how that was settled, which is the
step the workflow already prescribes for a package claim and which applies just as well to an API
claim.

A test now asserts both halves: that the code arrives, and that the SDK's sentence does not. The
second half is the one that matters — a test checking only that the call failed passes in exactly
the state this fixed.

## What this does not authorise

**Putting anything else in the filter.** It translates one exception type into text. A rule the
channel enforces belongs in `ChannelService`, where all three surfaces meet it, and a filter is a
tempting place to put one precisely because it is central.

**Leaking an exception that is not a `ChannelException`.** Everything else is still an unhandled
fault and still becomes the SDK's generic sentence, which is correct: an unexpected exception's
message is not written for a model and may carry detail a caller has no business reading.

**Reading this as making MCP an error-reporting surface with parity to REST.** It has no status
codes and it never will. What a caller gets is one line whose first token is the code, and code
that needs more than that should use REST.
