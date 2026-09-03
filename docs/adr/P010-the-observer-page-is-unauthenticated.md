# P010 — The observer page is served without authentication

## Context

`/ui` is a read-only panel showing who is waiting on whom. It needs the token to call the API, and
the token is what the viewer types into it.

## Decision

The page itself is served unauthenticated, as an `EmbeddedResource` inside the assembly. Every
call it makes carries the token the viewer supplied.

## Consequences

A page that required the token in order to load could not be the page where the viewer enters the
token. Embedding it in the assembly means publishing as a single file cannot leave it behind.

## What this does not authorise

**Any data in that page.** It is markup and script; every value on screen arrived through an
authenticated call made by the viewer's browser. The day a template renders an agent name into
the HTML, this record is what it violates.
