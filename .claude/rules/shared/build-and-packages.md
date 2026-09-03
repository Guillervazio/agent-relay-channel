---
paths:
  - "**/*.csproj"
  - "**/*.props"
  - "**/*.sln"
  - "**/*.slnx"
---

# Build and packages — base

Shared across projects. A deviation from any clause here is recorded under `## Deviations` in
`build-and-packages.project.md`, naming the clause it replaces, and that entry wins.

## Central version management

Package versions are declared **once**, in `Directory.Packages.props`. A `.csproj` references a
package by name with no version.

Settings shared by every project — target framework, language version, nullable, implicit usings,
warnings-as-errors, code-style enforcement — live in `Directory.Build.props` at the repository
root. An individual `.csproj` carries only what is specific to it: its package references, its
project references, and an output type where it needs one.

Create a missing project with the CLI, never by hand-writing a `.csproj`.

## Approval

**No package is added without explicit approval, however convenient it seems.** No preview or
experimental packages. No two packages covering one concern. Prefer a built-in over a third party.
A package is referenced only by the project that uses it, never solution-wide. Adding one means
updating the appendix in the same change.

**Raising a version needs approval too.** An approved package is approved at a version, not
forever: a major bump can change a licence, a test platform or a transitive dependency, and a
version declared here may be pinned in step with a tool version somewhere else — a container
stage, a CI image — that nothing will remind you about. Reviewing what is outdated is free;
acting on it is not.

Two ownership rules are structural rather than taste:

* **The domain project references no NuGet package at all**, beyond the base class library. It is
  the project every other one references, so a dependency added there reaches everything.
* **One project owns the ORM packages** and no other references them, which is what keeps
  persistence concerns from leaking upward through a type that happens to be in scope.

## What ships in the framework

Some assemblies come with the shared framework and **must not** be referenced: doing so fails the
restore with `NU1510`. That list is per-platform and lives in the appendix.

The rule this list exists to enforce is not "keep a list". It is: **a claim about what a dependency
costs is a claim, and it is checked by compiling, not by reading.** This repository carried
"a health check endpoint needs a package approval" for four increments; it was never true, and it
is what kept the entry looking expensive.

## The three tables the appendix must fill

```markdown
## Approved            | Package | Project | What it is for |
## Rejected            | Package | Reason it was refused |
## Pinned              | Package | Version | Why | What re-checks whether the pin is still needed |
```

Completeness: **every** `PackageVersion` in `Directory.Packages.props` appears as approved or as
pinned. Every pin declares what re-checks it — **and that instrument cannot be one that comes back
clean *because of* the pin.** A vulnerability report is clean while the pin holds, so it can never
report that the pin has become unnecessary; the package's own manifest can.

That last sentence is here rather than in the appendix because it is not a fact about any project.
It is about what makes a check valid.

## Migrations

Schema migrations live inside the project that owns the ORM, and are applied by a separate step
before the application starts — [H009](../../../docs/adr/house/H009-migrations-never-run-at-application-startup.md).
A dedicated console host for them is not needed: the ORM's own bundle command produces a
self-contained executable, run as a one-shot container the application waits on. Introduce a
project for it only if a deployment need justifies it, and record the decision first.

Test fixtures calling migrate directly are a different situation: one instance against a throwaway
container.
