# P022 — The base that came back

## Context

`architecture` was the one area here that rejected its base. `dotnet-house`'s
`shared/architecture.md` described a five-role Clean Architecture — Api, Application, Domain,
Contracts, Infrastructure — with aggregate-root repositories, vertical slices and handler-based
CQRS. ARC is three surfaces over one in-process service with no ORM, and would have had to deviate
from five of its clauses. The package's own exit criterion says two or more means the base was a
project decision in disguise, so the finding was recorded in [backlog.md](../backlog.md) as the
package's to fix, and [architecture.project.md](../../.claude/rules/architecture.project.md) was
written standalone.

The package fixed it. `architecture.md` was demoted to `shapes/clean-architecture.md`, which
nothing inherits, and the base was rewritten as the four clauses this repository and the
originating one had arrived at **separately**: the dependency graph written down as roles with its
forbidden edges named, an edge role that translates and does not decide, no new architectural
patterns with [H002](house/H002-single-implementation-interfaces.md)'s test on the first interface,
and a mandatory table of where a type goes.

That leaves a choice the finding did not anticipate, because it assumed the base would stay wrong.
The appendix here stands alone and works. Nothing breaks by ignoring the new base.

## Decision

**Adopt it, and the deviation count is the argument.** Read honestly against the new base, ARC
deviates from **none** of its four clauses — not by concession, but because two of them were
extracted from this repository's own appendix. Refusing a base whose every clause this project
already obeys would be keeping a private copy of a shared truth, which is the duplication the
base-and-appendix split exists to prevent.

**The count is the test in both directions.** Five was the reason to reject; zero is the reason to
adopt. That symmetry is the point: a base is adopted or refused by counting what it would cost,
never by whether having one feels tidier.

**Staying standalone had a specific cost, and it is the deciding one.** An area with no base sits
outside the deviation machinery. If ARC later diverged from something the other project depends on,
there would be no clause to name, no `## Deviations` entry to write, and nothing anywhere to notice
— the divergence would simply be this repository's opinion, indistinguishable from a decision
somebody took on purpose.

**The appendix loses what the base now says, and only that.** The reason behind the edge rule, the
restatement of H002's test, and the line about not writing down the folder tree were all moved out.
What stayed is every fact that is about ARC: which three roles, which two edges are not a leaf
pointing at the core and why, which type is the core, the named exceptions, and the table.

## Consequences

`CLAUDE.md` counts five paired areas and three unpaired ones, where it counted four and four. The
sentence that said this area rejected a base is gone from both files; what replaces it says the
base was demoted on this repository's evidence, which is the part worth keeping.

The exit criterion for the package's v0 asks that a second project never had to edit a base from
inside its own repository. That is still true, and this increment is the case that could have
broken it: the route taken was a finding reported and a package release, not a local edit of a
copy. The same route closed the `build-and-packages` `paths:` defect in the same version.

The `## Deviations` section in the appendix says *none*, which is a claim that can go stale. It
names its own condition: the base is short, and derived partly from here.

## What this does not authorise

**Adopting a base because having one is tidier.** The count is not a formality. A base adopted at
three deviations is a base being tolerated, and the appendix then carries three entries nobody
believes — which is worse than the standalone file this decision replaced, because it looks like
agreement.

**Editing a base from inside this repository.** The way this one changed was a finding reported and
a version shipped. That stays the only way, and the next disagreement with a base is worth the same
report rather than a local edit that no other consumer will ever see. The only thing a copy's
frontmatter may be adapted for is this project's own layout — never to correct the base, which is
what the `Dockerfile` case turned out to be.

**Reading this as the package being right.** The base changed because this repository disagreed
with it, in writing, for four increments. What the record shows is a disagreement resolved in the
place that owned it, not a rule that turned out to have been fine all along.

**Assuming the other three unpaired areas are next.** `protocol`, `persistence` and `concurrency`
have no base because the package has nothing on them, which is a different situation from having
rejected one. Nothing here says a base will appear for them, and one arriving would be judged by
the same count.
