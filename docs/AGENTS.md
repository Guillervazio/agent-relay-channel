# Instructions for the agents

**Read this only if the handshake does not reach you.** The hub already says all of it
itself: an MCP client receives it in the `instructions` field of `initialize`, so a
repository that uses ARC has nothing to write down and no copy of its own to keep true.
[PROTOCOL.md](PROTOCOL.md) describes the field.

Two cases it does not reach. An agent driving `arc` from the command line, which has no
handshake at all; and a client that receives `instructions` and does not put them in front
of its model, which is the client's decision and not something this project can promise
either way. For those, paste the section below into the `CLAUDE.md` (Claude Code) or the
`AGENTS.md` (Codex) of the repository they work in, and adjust the agent names.

It is deliberately the same advice as the handshake's, spelled out for the command line.
One second copy, here, is the price of covering a client that ignores the first; a copy per
repository adopting the channel is what the handshake exists to avoid.

---

## Communicating with the other agent

You are working in parallel with another agent, from a different provider and on a
different machine, over a clone of the same repository. You communicate through ARC, not
through files.

| Agent | Machine | Provider |
|---|---|---|
| `claude-pc1` | PC1 | Claude Code |
| `codex-pc2` | PC2 | Codex CLI |

### When to write to the other one

- **Ask (`arc_ask` / `arc ask`)** when you need their answer in order to go on: an API
  contract they define, a decision about code they are touching, confirming an assumption
  before building on it. It blocks until they reply.
- **Notify (`arc_note` / `arc note`)** when you are only reporting an accomplished fact:
  "I pushed the branch", "I changed the signature of this method". It expects no answer.
- **Do not write** to narrate your progress. The channel is for what the other one needs
  to know, not for keeping a diary.

### At the start of a turn

Check the mailbox before you get to work: the other agent may be blocked waiting for you
right now.

```bash
arc inbox
```

If a request has arrived, answer it before carrying on with your own work: every minute
you take is a minute the other one spends idle.

```bash
arc respond req_1a2b3c --body-file answer.md
```

### What to send in a message

Both machines have the same repository. **Send references, not content**:

```bash
arc ask --to codex-pc2 \
  --subject "Contract of the payments endpoint" \
  --body-file question.md \
  --refs '{"branch":"feat/payments","commit":"a1b2c3d","files":["src/payments/Total.cs"]}' \
  --wait 180
```

Before quoting code, push your branch: that way the other one can look at it in their own
clone. The body is limited to 256 KB.

### Writing the body

Always by file, never on the command line: on Windows the arguments go through the ANSI
codepage and accented characters get corrupted.

```bash
cat > question.md <<'EOF'
Does the `total` field travel in cents or in euros?
I need it to close the form validation.
EOF
arc ask --to codex-pc2 --body-file question.md --wait 180
```

### When the wait expires

It is not an error. The request is still alive and the other one will see it in their
mailbox. You have two options, and the first is almost always the right one:

1. Carry on with another part of your work and pick the answer up later:
   `arc await req_1a2b3c --wait 300`.
2. Wait again, if you genuinely cannot make progress without it.

You can also write to **yourself**, which is what to do when the thing you want to pick up
next turn is your own. `arc note --to <your own name>`, or `arc ask --to <your own name>
--wait 0`, leaves it in your own mailbox for the next turn. What you cannot do is wait on
it: `--wait` above 0 on a request to yourself is refused with `self_addressed`, because the
only one who could answer is you, blocked waiting.

**Never both wait at the same time**: you would burn through both turns without anyone
making progress. If you are going to ask something long, say so with `arc note` and keep
working.

### Exit codes

Branch on the code, not on the text:

| Code | Meaning |
|---|---|
| `0` | Answered / there are messages / the operation succeeded |
| `1` | Network or hub error |
| `2` | Incorrect use of the command |
| `3` | The wait expired with no answer |
| `4` | The mailbox is empty |

### If the channel does not answer

Check `arc health`. If the hub is not up, carry on with your work and leave a record of
what you would have asked; do not block the turn retrying.
