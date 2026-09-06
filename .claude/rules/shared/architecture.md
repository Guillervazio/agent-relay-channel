---
paths:
  - "src/**/*.cs"
  - "tests/**/*.cs"
---

# Architecture — base

Shared across projects. A deviation from any clause here is recorded under `## Deviations` in
`architecture.project.md`, naming the clause it replaces, and that entry wins.

**This base is thin on purpose, and it used to be four times longer.** It described Clean
Architecture with five roles, aggregate-root repositories, vertical slices and handler-based CQRS,
and the second project to consume this package would have had to deviate from five of its clauses.
By the [exit criterion](https://github.com/Guillervazio/dotnet-house#exit-criterion-for-v0) that is a project decision wearing a
base's clothes. The text was not lost — it is
[shapes/clean-architecture.md](https://github.com/Guillervazio/dotnet-house/blob/master/shapes/clean-architecture.md), a shape a project adopts rather than
a rule it inherits.

What is left is what two projects arrived at **independently**, in shapes that share almost nothing
else: one a layered web application over a relational database, the other three surfaces over a
single in-process service.

## The dependency graph is written down, as roles

The appendix draws it and maps each role to a project. Project names belong there; this file talks
about roles.

A drawing that says only what is allowed cannot be checked against anything, so two things are
mandatory in it:

* the edges **forbidden outright**, named as edges rather than implied by their absence;
* every edge that is not a leaf pointing at the core. Each of those exists for a reason and the
  appendix gives the reason, one per edge. That is where a test project reaching into a composition
  root gets declared, rather than discovered later by somebody who assumes it is a mistake.

A test project references only what it exercises. Needing a lower layer inside a unit test suite
means the test is not a unit test, not that a reference is missing.

## A role that translates does not decide

Every consuming solution has at least one edge role — an API, a CLI, a message handler, a scheduled
job — whose whole job is to bind an input, call exactly one thing behind it, and render the result
in its own idiom.

Forbidden in such a role: a decision the layer behind it could have taken, a second copy of a
validation, and reaching past its immediate neighbour for something that neighbour already exposes.

The reason is not tidiness. Where a solution has **more than one** edge, the failure is that two of
them answer the same question differently — which nobody notices, because no one person uses both.
The appendix says whether an operation must appear on every edge, and names each exception with its
reason. An edge that quietly lacks an operation is how two surfaces stop being the same thing.

## No new architectural patterns

No mediator, no dispatcher, no reflection-based registration, and no abstraction introduced ahead of
a second implementation. Reuse the pattern the repository already has: a second way of doing a thing
already done is a cost every later reader pays.

The first interface over a single implementation needs
[H002](../../../docs/adr/house/H002-single-implementation-interfaces.md)'s test — it must remove a constraint that
cannot be removed otherwise. "So a test can substitute it" is that test failing, not passing.

## Where a type goes

The appendix carries a table of *kind of thing* → *where it lives*. **It is mandatory.** It is the
part of an architecture rule anybody actually reads, because it answers the question at the moment
somebody has it, and it is what makes the two clauses above enforceable rather than agreeable.

The folder tree is written down in neither file. The solution file and a directory listing describe
it better and do not go stale.
