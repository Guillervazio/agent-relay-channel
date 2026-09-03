---
paths:
  - "tests/**/*.cs"
---

# Testing — base

Shared across projects. A deviation from any clause here is recorded under `## Deviations` in
`testing.project.md`, naming the clause it replaces, and that entry wins.

Every feature ships with tests. A feature without them is not done.

## The four categories, and what characterises each

* **Unit** — domain and application logic in isolation. No database, no network, no file system, no
  container, no DI container. Dependencies substituted. Very fast.
* **Integration** — repositories, DI wiring, orchestration, against a real database. A third party
  is still substituted: "integration" means the layers of this system, not somebody else's.
* **Persistence** — mappings, constraints, converters, indexes, **relationships and their delete
  behaviour**, transactions, query translation.
* **Scenario** — whole business scenarios through the real HTTP pipeline, asserting the response
  envelope and the rows behind it, **including the 401 and 403 paths**. An endpoint whose refusals
  are untested is an endpoint whose authorisation is untested.

A unit test suite never references the infrastructure project. Needing it means the test is not a
unit test.

Most tests are unit tests, and the count thins out as the categories get slower. It is the
proportion that keeps the fast gate worth running: a suite where every new test is a scenario test
conforms to every rule here and still takes minutes to tell you anything.

## The suite inventory the appendix must fill

```markdown
## Suite inventory

| Project | Scope | Isolates with a container? | In the fast gate? |
|---|---|---|---|
| <path to the .csproj> | <what it exercises> | yes / no | yes / no |
```

Completeness: **every** project referencing `Microsoft.NET.Test.Sdk` appears in that table. A
missing row means the appendix is incomplete, and that is an error to fix before going on. A
project without a container belongs in the fast gate; one with a container does not. This is the
same query the Stop hook's discovery runs — wherever that hook comes from, this repository's copy
or the plugin's — so the rule and the hook share one criterion rather than two copies of it.

## Tools

FluentAssertions for assertions. NSubstitute for substitutes, and only for dependencies that are
genuinely external — never a value object, an entity, a record or a plain DTO. `FakeTimeProvider`
for time; never assert against the real clock.

## Independence

No test depends on another having run, on the order they run in, or on state a previous one left
behind — and that is not only about database rows. A static mutable field, a fixture property one
test writes and the next reads, and a shared collection are the same coupling without a container
to reset. The suite with no container is the one most exposed to it.

## Naming and shape

`MethodName_Should_ExpectedBehaviour_When_Condition`.

**One action, one logical assertion.** That is what "too large" means concretely: a test exercising
two calls, or asserting two unrelated facts, is two tests.

The three phases are separated by a **blank line and nothing else**. Do not write `// Arrange`,
`// Act`, `// Assert`: a comment restating the code is noise in a test the same as anywhere. A test
that would need the labels to be readable is a test that is too large — split it, or move its
setup into a factory. What a comment on a test **is** for is the why, which no amount of reading
the assertions recovers.

## The database is real

A real engine in a container — [H008](../../../docs/adr/house/H008-real-database-in-tests-never-inmemory.md).
One container per test project, started once through a collection fixture, with migrations applied
at startup so the tests exercise the schema the application deploys. Isolation comes from
**resetting data between tests**, never from recreating the container, and no test may depend on
rows another test left behind.

## Comparing a value that made a round trip

A value that went through the database is not necessarily equal to the one that went in — a
`DateTimeOffset` comes back truncated, because .NET keeps 100-nanosecond ticks and the column keeps
microseconds. Assert with a tolerance whenever **one** side of the comparison was read from the
database and the other was not, and justify the tolerance by what the test is distinguishing.

It does not apply where both sides come from the same place. Two in-memory values are compared
exactly.

## When a test freezes a value and when it discovers it

A test **freezes** a literal when the test *is* the client: an error code asserted against its
own constant passes even when somebody changes the constant, which is the breaking change it
exists to catch — [H012](../../../docs/adr/house/H012-an-error-code-is-defined-once-and-a-test-keeps-the-literal.md).

A test **discovers** the value when its whole job is to detect divergence between two copies. A
hand-written list compared against a configuration file passes in exactly the case it was written
for: somebody adds the seventh item and edits neither the file nor the list. Reflect over the
source of truth instead.

## Reading a file from a unit test

"No file system" bars **runtime** I/O: a path the code under test reads, a temp file, anything
whose content depends on when or where the test runs. It does not bar a file checked into the
repository and copied to the output, which is a constant that happens to be stored as text.

The boundary is what the rule protects. A file the build produces, one outside the repository, or
one that differs per machine has left this case. Do not read this as "unit tests may touch disk".

## Coverage

Domain business rules require tests, and **an endpoint is covered when its failure paths are** —
the validation refusal, the not-found, the conflict. A new action with only its happy path tested
is a new action untested, because the happy path is the one that was written while looking at it.

There is deliberately **no percentage here**: a number nobody measures is worse than no target, and
adding coverage measurement is a decision with its own cost. Do not write a test to move a number.

## Not done

**No skipped test, no ignored test, no flaky test.** A suite reported green with a `Skip` attribute
in it is a suite whose report is false, and a test that passes on the second run is a test that
tells you nothing on the first. Delete it or fix it; parking it is the option that is not
available.
