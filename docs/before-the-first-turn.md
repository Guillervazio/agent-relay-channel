# Before the first turn

**A template to fill in, in the repository that is adopting ARC — not here.** Copy the four blocks
below into that project, answer them, and delete everything else on this page.

It exists because the channel has no notion of correctness. It guarantees delivery and it
guarantees a real wait; it does not guarantee that what travelled was true. A confident wrong
answer moves exactly as well as a right one, and it moves faster than anybody would catch it,
because the whole arrangement removed the person from the middle on purpose.

So two agents talking will settle whatever nobody settled. Not by being careless — by being asked.
Ask an agent what unit a field travels in and it will answer; if nothing decided that, the answer is
an invention, and the other agent will build on it because an answer over the channel looks exactly
like a fact.

**Fill this in before the first turn, and keep it short.** Every section below has a test for
whether the answer is real, and the tests matter more than the length. A project that answers all
four in one page is finished with this file.

> **The list of things to work on is deliberately not here.** It is the least of it. A list of items
> over an undecided seam produces invention at speed, which is worse than no list. Items go in that
> project's own backlog once the four blocks below are answered.

---

## 1. The seam

**What each agent owns, and where the two owns touch.**

The seam is what the agents will be asking each other about all day, so it is the thing that cannot
be left open. Everything else they can look up.

*The test:* could an agent answer a question **at the seam** without asking you? If the honest
answer is that it would have to guess, the seam is not defined yet, and the guess will arrive over
the channel looking like a decision.

*A bad answer:* "A does the frontend, B does the backend." That is a division of labour, not a
seam. The seam is the **shape of what crosses**: the payload, the contract, the file, the
interface — the thing that has to mean the same to both of them.

```markdown
## The seam

`<agent-a>` owns: …
`<agent-b>` owns: …

Where they meet: …
What crosses between them, and in what shape: …
```

---

## 2. Already decided

**The decisions at that seam that are made, so nobody re-makes them.**

Three or four. Not a corpus. The ones an agent would otherwise invent, which are almost always the
ones where a plausible answer exists and only one of them is yours.

Write each with **what it does not authorise**. That second half is what keeps a decision from
being read as a licence for the next thing that resembles it, and it is the part that gets skipped.

*The test:* if the two agents disagreed about this tomorrow, does this line settle it, or does it
need you?

```markdown
## Already decided

- **<the decision>.** <one or two sentences of why.>
  <br>Does not authorise: <the nearby thing this must not be read as permitting.>
```

---

## 3. Not theirs to decide

**What comes to you instead of being settled between them.**

This one is load-bearing in a way the channel will not help with: an agent's name is
**attribution, never authorisation** — [P004](adr/P004-one-token-and-an-agent-header.md). Nothing
in ARC stops one agent from deciding something that belonged to the other, or to you. If it is not
written here, it gets settled by whoever was asked.

Three kinds are almost always on this list, and the third is the one people forget:

* **Product and method.** What the thing is for, and what counts as a good answer.
* **Anything that widens what an agent may touch** on the machine.
* **What the next increment is**, and when to stop.

```markdown
## Not theirs to decide

These go to <the person>, through `<the lead agent>`:
- …
```

---

## 4. Finished

**What makes one item done — per item, not in general.**

This is what lets a conversation end by itself. Without it the exchange ends when you notice it has
gone quiet, which puts you back where the channel was supposed to take you out of.

*The test:* can the agent holding the conversation **close the item without asking you**?

*A bad answer:* "when it works." *A worse one:* "when `<agent-b>` replies" — an answer is not a
deliverable, and this is the specific failure the channel makes easy, because a good reply looks
like finished work until somebody opens the file.

```markdown
## Finished

An item is done when:
1. <the artifact exists, and where>
2. <it was answered over the channel, saying what was written and the verdict>
3. <it answers the question that was asked, not a nearby one>
```

---

## What this is not

**Not the channel's rules.** Those ship with the hub and arrive in the MCP handshake — check the
mailbox at the start of a turn, ask versus notify, references rather than content, never both wait
at once, and what you know goes in the file rather than only into the channel. A project adopting
ARC writes none of that down
([P014](adr/P014-the-channel-explains-itself-in-the-handshake.md),
[P025](adr/P025-what-earns-a-place-in-the-handshake.md)). If you find yourself copying any of it
into the new project, stop: it is already there, and a second copy is one that will drift.

**Not the per-agent file.** That is a separate, smaller thing — what *this* clone is, on what
branch, and whether that agent commits.
[arc-dev-environment.md §7](arc-dev-environment.md#7-what-goes-in-the-agents-own-file) says what
belongs in it, and the three files under [demo/](../demo/) are the shape to copy.

**Not a substitute for one agent going first.** The relay pays off when there is something to
divide. On a project starting from nothing, getting the seam right is work for a single head — so
the first increment of a new project is usually **one agent, producing sections 1 to 3 of this
file**. Two agents negotiating the seam over the channel is precisely the case where they invent,
and neither of them has the standing to close the argument.
