---
paths:
  - "**/*.cs"
---

# Coding conventions — base

Shared across projects. A deviation from any clause here is recorded under `## Deviations` in
`coding-conventions.project.md`, naming the clause it replaces, and that entry wins. A deviation
without a project ADR is not a deviation, it is drift.

## What the toolchain already enforces, and what it does not

`.editorconfig` plus `EnforceCodeStyleInBuild` and `TreatWarningsAsErrors` **fail the build** on:
explicit types over `var`, braces on every block, explicit access modifiers, file-scoped
namespaces, `using` outside the namespace, unmarked `readonly` fields, **symbol** naming, and
nullable violations. None of that is written down again here — the compiler says it louder and
sooner.

Symbol naming means types, members, parameters and locals. It does **not** cover file or folder
names: those are PascalCase by convention and nothing checks them.

Four more are corrected by `dotnet format` and **not** by the build: `using` order, whitespace,
redundant `this.`, and `System.Int32` for `int`. They are enforced only because the Stop hook runs
`dotnet format` every turn. **If that hook stops running, they stop being enforced by anything** —
and "stops running" no longer means a file was deleted. Where the gate arrives as a plugin, it also
covers the plugin failing to resolve or its entry in `enabledPlugins` being turned off, neither of
which announces itself. Nothing here tells you the conventions went unenforced; you have to check.

One convention nothing checks: an asynchronous method ends with the `Async` suffix.

## Time

`DateTimeOffset` always. Never `DateTime`, `DateTime.Now` or `DateTime.UtcNow`. The current time
comes from an injected `TimeProvider`, which is what makes it substitutable in a test. `DateOnly`
and `TimeOnly` where the value genuinely has no time-zone dimension.

## Domain models

* Aggregates and entities: classes with private setters and behaviour-bearing methods.
* Value objects: records — see [H001](../../../docs/adr/house/H001-no-value-object-base-type.md).
* Identifiers: strongly typed, never a bare primitive.
* Construction: a static factory that enforces the invariants, not a public constructor.
* Types not designed for inheritance are `sealed`. That is the default, not the exception.
* Contract records expose primitives, never domain types.

## Missing versus refused

A query handler returns `null` for a resource that does not exist and the transport turns that
into a 404. A command handler on a missing resource throws, because it cannot fulfil its contract.
Do not use an exception for the query case.

## Nullability

Never suppress a warning with `!` unless it is provably safe, and write the reason in a comment
where you do.

## Asynchrony

Every method that performs I/O takes a `CancellationToken` and passes it down. No analyser enforces
this by default — and neither of the two below is caught by anything either:

* **Never `.Result` or `.Wait()`.** Blocking on a task holds a request thread and deadlocks under
  a synchronisation context.
* **Never `async void`** outside an event handler. Its exception cannot be caught by the caller and
  takes the process down.

## Exceptions

* **Never swallow one.** An empty `catch` is silent to the compiler and to every analyser here.
* **A domain exception derives from the layer's common base type.** One that does not is not
  recognised by the error mapping, so a deliberate business refusal is served as a 500.
* **Never for expected control flow**, and log an unexpected one **once**, at the boundary that
  handles it. Logging and rethrowing gets the same failure recorded twice.

## Dependency injection

* **Constructor injection only.** No property or setter injection.
* **Never inject the service provider** into application code to resolve dependencies on demand.
  That is a service locator, and it hides what a type actually needs.
* **Lifetimes are a correctness concern, not a tuning one.** Anything holding per-request state —
  the persistence context, repositories, handlers, validators — is scoped. A captive dependency
  (a singleton holding a scoped one) leaks state across requests, and no test in a suite reliably
  surfaces it.

## Size and shape

One responsibility per method, early returns over nested conditionals, and nesting no deeper than
three. Nothing enforces any of this; it is the part a reviewer has to carry.

## Magic values

No bare numbers or strings with meaning. A published identifier is a constant, a bounded set is an
enum, a value with rules is a value object. This is what keeps a code, a permission or a threshold
from acquiring a second, different copy somewhere else.

## Static classes

A static class is justified when there is nothing an instance would hold: an extension container,
a registration class, or a compile-time constant table plus the lookups that read it.

The boundary is the part worth stating. A type that acquires a dependency, or holds a value that
varies per request or per environment, has left this case and is a scoped service. Configuration
read at startup is the tempting mistake — it is not a constant, it only looks like one from inside
the method reading it.

## Comments

Comments explain **why**, never **what**. A comment restating the code is noise. No commented-out
code, and no `TODO`: what is deferred is recorded outside the code, and the project appendix says
where.
