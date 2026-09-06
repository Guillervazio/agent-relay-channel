# Increment 11 — the base that came back

Three findings had been sitting in [backlog.md](../backlog.md) waiting on the same thing: a commit
in `dotnet-house`. All three were reported and fixed there, and this increment is what the fixes
cost here — which turned out to be more than copying two files.

The largest reversed a standing position. `architecture` was the one area that had **rejected** its
base: five deviations, a standalone appendix, and a finding saying the base was a project decision
in disguise. The package agreed, demoted it to a shape nothing inherits, and rewrote the base as
the four clauses this repository and the originating one had reached separately. ARC now deviates
from **none** of them, and adopting it was still a decision with a real alternative —
[P022](../adr/P022-the-base-that-came-back.md).

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | Adopt the new architecture base, and stop the appendix repeating it | done | this commit |
| 2 | Take the two other fixes: the `build-and-packages` paths, the three `H###` copies | done | this commit |
| 3 | Close: the record, the three findings, and the routing table those fixes change | done | this commit |

Verified at close (7 September 2026): `dotnet build` **0 warnings, 0 errors**; `dotnet test`
**133 passed, 0 failed, 0 skipped**; `dotnet format --verify-no-changes` clean. No code changed, so
`scripts/test-all.sh` was not re-run and increment 09's run of it stands.

Verified by hand, since no test reaches any of it: the four `shared/` copies differ from the
package's files only where a copy is allowed to differ, checked with `diff`; every relative link in
the edited files resolves, checked mechanically; and the appendix was read clause by clause against
the base rather than trusted to have stopped overlapping.

**One thing was not verified at close and [todo.md](../todo.md) said so:** the copies were taken
from the package's branch before it was merged. It merged the same day as `c96f84a`, shipping
0.3.0, and the diff was run against that `master`: all five copies and the thirteen `H###` records
are what shipped.

---

## What the plan got wrong

| The plan said | What was true |
|---|---|
| Three findings, three fixes in the package | Two. The `H###` records had already been corrected there in 0.2.x — it was ARC's copies that were stale, which nothing here could have told you: a copy that has fallen behind reads exactly like a copy that is current. Only a `diff` against the source says otherwise, and this increment is the first time one was run |
| Adopting the demoted base is bookkeeping: the appendix already stands alone | It is a decision, and the alternative was live. Staying standalone costs something specific — an area with no base sits outside the deviation machinery, so a future divergence would have no clause to name and nothing anywhere to notice. That argument, not tidiness, is what P022 turns on |
| The appendix loses its opening paragraph and keeps the rest | Three passages had to go, and finding them meant reading the base clause by clause: the reason behind the edge rule, now the base's; the restatement of H002's test; and the line about not writing down the folder tree. Two of them were rewritten rather than deleted, because ARC has something concrete to say where the base is general — three edges, and zero interfaces in `Arc.Core` |
| The `Dockerfile` fix is a `paths:` line | It was a defect **in the base**, not in this repository's copy. That distinction is the whole of what the package release bought: adapting a copy's frontmatter to a project's layout is legitimate, and doing it because the base is wrong is a bug report waiting to be filed. Increment 10's routing table had recorded the workaround as if it were permanent |

## What was decided

[P022](../adr/P022-the-base-that-came-back.md). Adopt, because the deviation count is zero and the
count is the test in both directions: five was the reason to reject, zero is the reason to adopt.
A base adopted at three deviations would be a base being tolerated, and the appendix would carry
three entries nobody believes.

The route matters as much as the outcome. The package's exit criterion asks that a second project
never had to edit a base from inside its own repository, and this increment is the case that could
have broken it: what happened instead was a finding reported, a demotion argued in the package, and
a version shipped.

## The rules it made false

* **`CLAUDE.md`, twice.** Four paired areas became five, and four unpaired ones became three. The
  sentence saying `architecture` replaces "a base that did not survive contact with this project"
  was true when written and is now the story of how the base changed, which is the part worth
  keeping.
* **`CLAUDE.md`'s routing table**, two rows. Increment 10 had split `global.json`, the `Dockerfile`
  and the workflows onto a row that said *open the base by hand, it cannot carry these paths*. It
  can now. That row was correct for one day and is the shortest-lived clause in this repository.
* **`architecture.project.md`'s first paragraph**, which announced that the area had no base.

Checked and standing: `protocol`, `persistence` and `concurrency` still say they have no base, and
still should — the package has nothing on them, which is a different situation from having rejected
one.

## What this increment did not fix, deliberately

**The `paths:` of `shared/api-guidelines.md`**, which still names ARC's files rather than the
package's. That is the one thing every consumer is told to edit and it is not drift.

**Anything about the other three backlog findings.** The delivery pair and the service installation
are untouched; nothing here goes near them.

**Re-taking the copies after the package merges.** It is a `diff`, it takes a minute, and it is in
[todo.md](../todo.md) rather than done here because the merge had not happened.
