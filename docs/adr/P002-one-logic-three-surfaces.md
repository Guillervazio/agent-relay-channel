# P002 — One logic, three surfaces

## Context

The channel is reachable over REST, over MCP, and from a command line. Each is the natural door
for a different caller: a script, a model, a person.

## Decision

`ChannelService` holds every rule of the channel. `Arc.Hub` (REST and MCP) and `Arc.Cli`
translate: bind input, call one method, render the result in their own idiom.

## Consequences

A rule changes in one place and all three surfaces change with it. A new operation is expected on
all three, and an appendix has to say why if it is not.

The cost is that a surface cannot optimise for itself — the CLI cannot skip a validation it knows
will pass.

## What this does not authorise

Read-only projections for the observer panel go straight to `MessageStore`, and that is the whole
exception: no decision, no rule, nothing changed. A read that has to decide who may see it is a
channel operation — which is what took `GET /v1/messages/{id}` and `GET /v1/threads/{id}` back
behind `ChannelService` in increment 07
([P016](P016-a-message-is-read-by-its-two-ends.md)). `.claude/rules/architecture.project.md` draws
the line and holds the current list.
