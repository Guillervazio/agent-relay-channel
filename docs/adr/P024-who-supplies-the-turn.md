# P024 — Who supplies the turn

## Context

The channel makes a turn **wait**. `ask` holds the caller's turn open until the other side answers,
and that is the half [P001](P001-long-polling-not-a-broker.md) argued for and the hub delivers.

Nothing creates a turn. A message that lands in an idle agent's mailbox sits there until something
gives that agent a turn, and until now that something has always been a person opening a console.

**The demo in this repository is the proof, and it demonstrates both halves in one run.**
[demo/codex-b/AGENTS.md](../../demo/codex-b/AGENTS.md) opens by telling that agent to block in its
mailbox; [demo/claude-a/CLAUDE.md](../../demo/claude-a/CLAUDE.md) hands the other one a question
already written. The exchange that follows needs no person at all — and getting to it needs two
consoles started by hand. The channel works and the session does not.

So the purpose the README states is met by the channel and not by the system. The person stopped
being the transport and became the scheduler, which is a smaller job and still a person in the
middle of every exchange.

Nothing written down forbade closing this. P001's own *what this does not authorise* says it is an
argument about a client that does not exist between turns rather than an argument against brokers,
and that the reasoning stops applying the day something on the other end is long-running. It
declines to make the **transport** a broker. It says nothing about who opens a turn, and the claim
that supplying turns is outside this project came from a document written elsewhere, not from here.

## Decision

**A turn is supplied on the agent's own machine, and never by the hub.** The deployment the README
describes is two PCs; a hub on one of them cannot start a process on the other. That is not a
preference between designs, it is what the topology allows. Turn supply is therefore not a change
to the wire, not a fourth surface, and nothing `ChannelService` decides.

**The agent holding the conversation opens the other's turn when it needs an answer.** It queues
with `wait = 0`, starts the other agent's turn, and only then blocks with `arc await`. One `go`
from a person carries a whole increment, because `ask` and `await` block *inside* the caller's own
turn: the lead can chain ask, block, review and ask again for as many rounds as the work takes.

**The turn command is configuration** — per agent, per machine — and it names some provider's CLI.
**Nothing in this repository names one.**

## Consequences

Nothing runs while nothing is happening. The alternative shape is a loop that opens a turn every
interval and costs one whether or not the mailbox has anything in it, plus a thing to remember to
stop; this shape costs a turn exactly when a turn is needed.

**Only the lead can initiate, and that is a real narrowing.** An agent whose turn was opened to
answer a question can answer it, and can say something with `arc_note` while it is there, but it
cannot raise something an hour later on its own. Where both sides must be able to start an
exchange, this shape is the wrong one and the loop is the right one.

Deadlock gains a way in that used to be hypothetical. Two agents each waiting on the other is what
the handshake means by *never both wait at once*, and `waiters` on `/healthz` is where it is
visible; with turns opened automatically, nobody is watching a console when it happens.

The demo stops needing two consoles, which is the observable part of this record.

## What this does not authorise

**The hub learning to run a command.** The cross-machine argument settles it, and a hub that
executes something local when a message arrives is a different thing to secure than one that
stores the message: it would turn one shared token into arbitrary execution on every machine on
the channel.

**A subcommand of `Arc.Cli` that starts a turn.** A surface binds an input, calls exactly one
`ChannelService` method and renders the result —
[architecture.project.md](../../.claude/rules/architecture.project.md). Spawning a provider's
process is none of those, and the CLI is the surface an agent already runs *inside* a turn.

**Committing a provider's invocation.** Naming a provider in prose is what this repository has
always done and is not the problem; the executable, its flags and its sandbox settings are, because
those are what a version changes underneath you. They would have no row in
[build-and-packages.project.md](../../.claude/rules/build-and-packages.project.md) and no
instrument that could re-check them, which is why the turn command is configuration and why the
demo carries a placeholder rather than a command that runs.

**Reading this as making the second agent a service.** It is still a CLI agent that exists only
during its turn. What changed is who opens the turn, not how long it lasts, and P001 is untouched.
