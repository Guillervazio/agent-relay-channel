# Increment 10 — the rules that never arrived

`CLAUDE.md` said the rules in `.claude/rules/` were "loaded automatically when a matching file is
read". That is true of the mechanism and was false of the practice: a rule with `paths:` is
injected only when a tool **opens a file by path**, and reading the tree with `cat`, `sed` or
`grep` opens nothing.

Counted over the ten sessions this repository had had, seven of them worked with **no rule in
context at all**. Nothing said so at the time, and nothing would have: a turn that obeyed no rule
looks exactly like a turn that had none to break.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | The routing table on the one page that is always loaded, the two `paths:` gaps, and the record | done | this commit |
| 2 | Close: the log, the verification that cannot be done from here, and what the backlog now owes the package | done | this commit |

Verified at close (6 September 2026): `dotnet build` **0 warnings, 0 errors**; `dotnet test`
**133 passed, 0 failed, 0 skipped**; `dotnet format --verify-no-changes` clean; `dotnet restore
--force` clean, no advisory. No code changed, so `scripts/test-all.sh` was **not** re-run and
increment 09's run of it stands.

Verified by hand, because no test reaches either: every relative link in the edited files resolves,
checked mechanically; and the two glob shapes added to a `paths:` block match what they are meant
to, checked against git's own matcher rather than by reading them — `.github/workflows`, which is
how the harness stores `.github/workflows/**` after stripping the trailing `/**`, matches
`.github/workflows/gate.yml`.

**The one thing this increment is about is not verified, and cannot be from here.** Whether a rule
now loads for a file that did not match before can only be seen in a session that has not already
opened that rule by hand. [todo.md](../todo.md) carries the check.

---

## What the plan got wrong

| The plan said | What was true |
|---|---|
| The fix is coverage: widen `paths:` until the rules reach the files | Coverage is the smaller half. What decides whether a rule arrives is **which tool opened the file**, and no `paths:` entry reaches that. The two gaps closed here are real and would have changed nothing on their own — seven sessions failed with the coverage they already had |
| The bases and their appendices share one `paths:`, so widening one means widening both, and a base is edited in `dotnet-house` | They already did not share one: `testing.project.md` covered `scripts/**` and its base never has. So the invariant was false before this increment, the divergence is legitimate — a path that exists only here cannot go in a file edited elsewhere — and what was missing was saying so. It also turned a planned edit into a backlog entry the package owns |
| The claim to fix is "loaded automatically when a matching file is read" | That one, and a second nobody had questioned in the same paragraph. Grepping for the *assertion* rather than for the feature is what turned it up, which is `reconcile-rules` working exactly as it says |
| Verification is counting the loads in the next session's transcript | Right, and for a reason found the hard way. Three checks inside this session looked like a broken glob — `**/*.csproj` failing to load a rule it plainly matches — until the cause turned out to be that **a rule already opened by hand is never injected**. The mechanism was working; the test was contaminated. A verification that can produce a false negative that convincing is worth naming in the record |

## What was decided

[P021](../adr/P021-a-rule-that-never-arrived.md), in four parts: `CLAUDE.md` routes, because it is
the only file always in context, and it names files without quoting a clause so nothing acquires a
second home; the shell is not how a governed file is opened, which is now a clause rather than a
habit; two `paths:` gaps closed and no more, the test being whether that rule has something binding
to say about that file; and the documentation left uncovered on purpose, because what governs a
record or a spec is a procedure — `close-increment` and `reconcile-rules` — and not an area.

The table is a precondition, not an index. A rule that arrives mid-change can only be checked
against what is already written, which is the expensive half of the work and the half that gets
skipped.

## The rules it made false

Both were in `CLAUDE.md`, in the same paragraph, and one of them had been false for longer than the
change that exposed it.

* **"Loaded automatically when a matching file is read."** Rewritten into the section that now
  carries the measurement and the table.
* **"Four areas are two files sharing one `paths:`."** Already false: `testing.project.md` covers
  `scripts/**` and `shared/testing.md` does not. It now says the appendix may carry paths the base
  cannot, that two of them do, and what follows from it — where they differ, opening such a file
  loads the appendix alone, and the appendix's first line is the link to its base.

Checked and standing: no other copy of the loading claim exists. `README.md` describes the channel
and not the rule mechanism; `docs/AGENTS.md` addresses agents using ARC and not agents working on
it; `specs/01-arc-adopts-the-house-doctrine.md` names the four-layer chain and asserts nothing
about how it loads. `testing.project.md`'s section on the gate arriving as a plugin is the same
failure shape running the other way, and it stays as it is — the fact has one home, and it is now
`CLAUDE.md` with the record behind it.

## What this increment did not fix, deliberately

**The volume.** Twelve rule files counting the four bases, over 1,500 lines, about 57 KB of it
arriving at once when a file in
`Arc.Hub` is opened, in prose written to be read once by a person rather than skimmed under load.
Splitting the binding clauses from their argument is a real option and a large one, and doing it in
the same increment as the routing would have made it impossible to tell which of the two was worth
it. The measurement to take first is whether rules now arrive at all.

**Surviving a compaction.** A rule is injected once per session and does not come back. Nothing in
this repository can change that; what it can do is say so, which
[P021](../adr/P021-a-rule-that-never-arrived.md) does — if a decision turns on a clause and the
change is long, open the file again rather than recalling it.

**The base's frontmatter.** `shared/build-and-packages.md` carries the clause about a version
pinned in step with a container stage or a CI image, and it is that base which will not load when
the `Dockerfile` is opened. The fix is a commit in `dotnet-house`, so it is a finding in
[backlog.md](../backlog.md) alongside the two already waiting to be reported, not an edit made from
inside this repository.
