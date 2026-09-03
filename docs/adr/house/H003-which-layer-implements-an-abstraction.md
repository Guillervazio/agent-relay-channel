# H003 — The layer that implements an abstraction is the one that already owns the dependency

## Context

An application layer declares interfaces for what a use case needs but does not own. The folder
convention suggests the infrastructure project implements all of them, and following it uniformly
is the tempting default.

## Decision

The implementing layer is whichever one already owns the dependency the abstraction hides. Ask
what is being hidden, not what the folder is called.

## Consequences

A database abstraction is implemented by the layer that owns the database. A request-identity
abstraction is implemented by the layer that owns the request. Pulling a web framework into an
infrastructure project to satisfy a folder convention would trade a real coupling for a cosmetic
one.

## What this does not authorise

It does not license a higher layer implementing an abstraction because it is convenient to write
there. The dependency being hidden has to genuinely live in that layer, and the dependency
direction still holds in both cases.

## Evidence outside this project

Clean Architecture's dependency rule, and the Dependency Inversion Principle it comes from: the
abstraction belongs to the caller, the implementation to whoever owns the detail.
