# The demo

Two agents from different providers finishing a piece of work between them, from **one** sentence
typed by a person into **one** console.

That is the part worth watching, and it is the part this demo used to be missing. Until
[P024](../docs/adr/P024-who-supplies-the-turn.md) it needed two consoles started by hand: one agent
was told to block in its mailbox and the other was told to ask it something. The exchange after
that needed nobody, and getting to it needed you twice.

## The three directories

| Directory | Agent | Role |
|---|---|---|
| [claude-a/](claude-a/) | `claude-a` | **The lead.** You talk to this one, once |
| [codex-b/](codex-b/) | `codex-b` | The counterpart, a different provider |
| [claude-b/](claude-b/) | `claude-b` | The same counterpart, same provider as the lead, for when you have only one CLI installed |

Use `codex-b` **or** `claude-b`, not both. The lead's instructions name which one it is talking to.

## What has to be true first

1. **The hub is running** and you have its token. `./scripts/start-hub.ps1` prints both; the demo
   reads the token from `demo/token.txt`, which is not versioned.
2. **Both agents can reach the channel** — the MCP server registered, or `arc` on the path. The
   root [README](../README.md) covers registering it once per user.
3. **The lead knows how to open the counterpart's turn.** This is the one thing you configure, and
   the next section is only about it.

## The turn command

The counterpart does not exist between turns. When the lead needs an answer it starts the
counterpart's turn itself, and the command that does that is **configuration**: it belongs to your
machine and to whichever CLI you have installed, at whatever version you have installed it.

Set it once, in the environment the lead runs in:

```bash
# The command that gives the counterpart exactly one turn, and returns when that turn ends.
# Yours will name a real CLI, its working directory and its sandbox settings.
export ARC_TURN_CODEX_B='<your CLI> <run-once flags> --cd <path to codex-b> <prompt flags>'
```

**This repository ships a placeholder and not a command**, on purpose. A real invocation pins
somebody else's tool at a version nothing here tracks and nothing here could re-check —
[P024](../docs/adr/P024-who-supplies-the-turn.md) says why that stays out of the tree.

Two things worth knowing before you write yours, both of which cost an hour to discover:

* It has to run **one turn and exit**, not open an interactive session. Every CLI spells that
  differently.
* It has to be **non-interactive**. A turn that stops to ask you for approval has put you back in
  the middle, which is what this whole arrangement removes.

## Running it

Start the hub, then open one console in [claude-a/](claude-a/) and say:

```
empezá
```

Then stop. The lead reads its pending work, picks one item, and from there on the two of them talk
to each other. Watch it at `/ui`, which shows who is blocked right now with the clock running.

## What it does not do

The demo works from a short list of pending items written into the lead's own instructions, so a
trial run changes nothing in this repository. In real use that list is
[docs/todo.md](../docs/todo.md) and [docs/backlog.md](../docs/backlog.md), and the shape of the
session is the same.
