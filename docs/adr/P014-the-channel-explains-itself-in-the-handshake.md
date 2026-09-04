# P014 — The channel explains itself in the MCP handshake

## Context

Using ARC from a real project meant pasting a block of rules into that project's `CLAUDE.md` and
`AGENTS.md`: check the mailbox at the start of a turn, ask versus notify, send references rather
than content, never both wait at once. One copy per repository adopting the channel, and copies
drift — the second repository to adopt it would already be reading a version of the rules the
first one had moved past.

Four ways to stop that, all real:

* Keep pasting, and accept the drift.
* An MCP **resource** the client fetches on demand. Nothing obliges a client to fetch it, and a
  resource nobody reads is worse than a paragraph nobody pasted, because it looks solved.
* An **`arc_howto` tool**. A tool the model has to think of calling before it knows what the
  channel is, which is the wrong way round.
* The **`instructions` field of `initialize`**, which the protocol already defines for exactly
  this and `ModelContextProtocol` 2.2.0 exposes as `McpServerOptions.ServerInstructions`.

## Decision

`ServerInstructions`, from one constant in `Arc.Hub/ArcInstructions.cs`. The consuming repository
writes nothing: `claude mcp add --scope user` and `~/.codex/config.toml` are both global to the
user, so adopting the channel touches no project file at all.

The text names no project, no machine and no agent — a test asserts that, because a concrete agent
name would tie it back to one setup, which is what it exists to avoid. Who is at the other end is
discovered with `arc_agents`.

## Consequences

There is one copy of the rules and it ships with the hub, so changing them is a deploy rather than
a pull request against every repository on the channel.

**What the hub guarantees is that the field arrives and is not empty.** Whether a client puts that
text in front of its model is the client's decision, and `PROTOCOL.md` says so rather than
promising an injection this project does not control.

The handshake is MCP's alone. REST has none and the CLI has no session, so an agent driving `arc`
from the command line never sees a word of it — which is why `docs/AGENTS.md` survives, demoted
from "the text to paste into every repository" to the fallback for where the handshake does not
reach. That is one second copy, here, rather than one per consuming repository.

## What this does not authorise

Putting a **rule** there. `ServerInstructions` is prose a model may ignore with nothing failing,
and two of the three surfaces never receive it; a constraint the channel actually enforces belongs
in `ChannelService`, where all three meet it.

Nor does it authorise repeating what a tool's `[Description]` already says. The catalogue
describes the tools; the instructions describe the channel.
