# Increment 16 — before the first turn

The channel has no notion of correctness. It guarantees delivery and it guarantees a real wait; it
does not guarantee that what travelled was true. A confident wrong answer moves exactly as well as
a right one, and faster than anybody would catch it, because the arrangement removed the person
from the middle on purpose.

So two agents settle whatever nobody settled — not by being careless, but by being asked. Ask an
agent what unit a field travels in and it answers; if nothing decided that, the answer is an
invention, and the other agent builds on it because an answer over the channel looks exactly like a
fact.

[before-the-first-turn.md](../before-the-first-turn.md) is what a project adopting ARC fills in so
that does not happen. Four blocks: the seam, what is already decided at it, what is not theirs to
decide, and what makes one item finished.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | The template, its two routing rows, and the backlog entry that grew | done | this commit |

Verified at close (9 September 2026): `dotnet build` **0 warnings, 0 errors**; `dotnet test`
**136 passed, 0 failed, 0 skipped**; `dotnet format --verify-no-changes` clean; `dotnet restore
--force` clean, no advisory. No code changed, so `scripts/test-all.sh` was **not** re-run and
increment 12's run of it stands. Every relative link and the one cross-page anchor resolve, checked
mechanically.

**Nobody has filled it in.** That is not its own backlog entry: it joined the one that was already
there, because increments 13 to 16 have now shipped a demo nobody has run, a field report nothing
here can re-check and a template nobody has used, and three entries would have described one thing
three times.

---

## What the plan got wrong

| The plan said | What was true |
|---|---|
| The prerequisite is a defined backlog | It is not, and saying so is most of what this page does. A list of items over an **undecided seam** produces invention at speed, which is worse than no list — the agents are asked about the seam all day and answer from nothing. The items are the fifth thing to write and the template deliberately does not hold them |
| Four sections to fill in | Four sections **and a test each**, which is the half that makes them fillable. "What each agent owns" gets answered with a division of labour — *A does frontend, B does backend* — and that is not a seam. The test is whether an agent could answer a question at the seam without asking you, and it is what turns a heading into something with a wrong answer |
| A template for a new project should be minimal | Minimal, and it has to say **what does not go in it**, or the first thing anybody does is copy the channel's own rules into the new repository. That is the copy P014 exists to prevent, and the guide adopted in increment 15 arrived having made exactly that mistake. The page ends by naming what the handshake already carries |
| It is a document for this repository | It is a template to be filled in **elsewhere**, which changes what maintaining it means: the thing to protect is that it stays short and stays empty. That is now its row in `CLAUDE.md`'s table, because the failure mode is somebody here answering the questions on behalf of a project that has not been written yet |
| Two agents, then the seam | The other way round, and the page says so. Two agents negotiating the seam over the channel is precisely the case where they invent, and neither has the standing to close the argument. The first increment of a new project is usually one agent producing sections 1 to 3 |
