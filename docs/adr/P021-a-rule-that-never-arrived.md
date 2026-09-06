# P021 — A rule that never arrived

## Context

The eight files in [.claude/rules/](../../.claude/rules/) carry `paths:`, which makes them
conditional: the harness loads one only when a tool opens a file matching its globs. CLAUDE.md
described that as "loaded automatically when a matching file is read", which is true of the
mechanism and was false of the practice.

Measured on 6 September 2026 over the ten sessions this repository has had, counting the loads the
harness records rather than the ones intended: **seven of them loaded no rule at all.** The two
that loaded any are the two that opened source files with the tool that opens a file by path. The
seven that loaded none read the tree through the shell — between 37 and 173 shell calls each — and
`cat`, `sed` and `grep` open nothing the harness can see. One of those sessions made 25 edits and
4 writes, five of them to `scripts/start-hub.ps1`, which matches
[testing.project.md](../../.claude/rules/testing.project.md), and loaded nothing.

Three properties compound it, and each is worth naming because each survives the obvious fix:

* **Coverage.** 77 of the 122 versioned files match no `paths:` at all: every record in
  [adr/](.), every spec in [specs/](../specs/), `todo.md`, `backlog.md`, `README.md`, `CLAUDE.md`
  and the rules themselves. This repository is 65 markdown files against 26 C# ones, so most of
  the work happens where no rule is reachable however it is opened.
* **Once per session.** A rule is injected the first time a matching file is opened and not again.
  A compaction after that drops it, and nothing brings it back.
* **In bulk and late.** Opening one file in `Arc.Hub` injects seven rule files at once — about
  57 KB — and it arrives after the approach has been chosen, when a rule can only be checked
  against what is already written.

None of this announces itself. A turn that obeyed no rule looks exactly like a turn that had none
to break, which is the shape
[testing.project.md](../../.claude/rules/testing.project.md#the-gate-that-runs-every-turn-is-not-in-this-repository)
already names about the Stop gate, running the other way.

## Decision

**CLAUDE.md routes; the rules stay where the rules are.** It is the only file always in context,
so it carries a table of *what you are about to touch* → *what to open before deciding*. The table
names files and quotes no clause, so no fact acquires a second home. It is a precondition rather
than an index: opening the rule after the change is written is the expensive half of the work and
the half that gets skipped.

**The shell is not how a governed file is opened.** Reading or editing through `cat` and `sed`
works and costs every rule that governs the file, silently. That is now a clause under *Working
here* rather than a habit.

**Two `paths:` gaps closed, and no more.** `.github/workflows/**` on `testing.project.md`, which
is where *CI is not that gate* is settled; `Dockerfile` and `.github/workflows/**` on
`build-and-packages.project.md`, whose base names exactly this case — a version declared there
"pinned in step with a tool version somewhere else, a container stage, a CI image, that nothing
will remind you about".

**The documentation is left uncovered on purpose.** The records, the specs, `todo.md` and
`backlog.md` get no `paths:` entry, because what governs them is a procedure and not an area: the
`close-increment` and `reconcile-rules` skills. The table says so, which is cheaper and truer than
a rule file existing in order to be matched.

## Consequences

CLAUDE.md gets longer, and it is the one page whose length is always paid for. That is the trade
this record makes knowingly: a table nobody loads is worth less than a table everybody loads and
half of them skim.

The invariant CLAUDE.md stated — four areas as two files sharing one `paths:` — was already false
before this change. `testing.project.md` covered `scripts/**` and its base did not, because a path
that exists only in this project cannot be added to a file edited in `dotnet-house`. That is now
written down, along with what it means: where they differ, opening such a file loads the appendix
alone, and the appendix's first line is the link to its base.

Nothing in the harness changed, and nothing here repairs the loading. The shell still loads
nothing; the router exists **because** the mechanism cannot be relied on.

Verification is not that the next turn looked fine. It is counting the loads recorded in the
following session's transcript and seeing a number that is not zero — the same standard the gate
is held to, where what settles it is watching it block a turn.

## What this does not authorise

**Copying a clause into CLAUDE.md.** The table names files. The moment a rule's content is
summarised there it has a second home, the two disagree, and nobody can tell which was meant —
which is what [architecture.project.md](../../.claude/rules/architecture.project.md) exists to
prevent, applied to prose instead of to code.

**Adding a `paths:` entry so a rule "loads more often".** The test is whether that rule has
something binding to say about that file. A rule that says nothing about the `Dockerfile` does not
become useful by arriving with it, and every irrelevant clause loaded is attention taken from the
ones that apply.

**Treating the table as the rule.** It says which file to open. Obeying it is opening the file.

**Assuming a rule is in context because a file matched it.** It is injected once per session and
does not survive a compaction. If a decision turns on a clause and the change is long, open the
file again rather than recalling it.
