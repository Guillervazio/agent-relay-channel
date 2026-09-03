# P011 — A well-formed request that a rule refuses answers 422

## Context

[H013](house/H013-422-for-a-well-formed-request-that-fails-validation.md) separates "could not be
read" from "was read and refused". ARC answers 400 to both.

## Decision

ARC adopts H013. A body that could not be parsed answers 400; a body that parsed and was refused
by a rule answers 422.

## Consequences

A client can tell a bug in its own serialisation from a rule it broke, without reading the error
code — which matters most for the MCP surface, where the caller is a model deciding whether to
retry.

Four codes move: `self_addressed`, `empty_body`, `body_too_large`, `bad_recipient`. `invalid_json`
stays 400 and is the case H013 exists to distinguish. `bad_agent` on the `X-ARC-Agent` header is
undecided and is settled by the commit that moves the other four.

**This is a target, not the current state.** The code answers 400 today, and
`.claude/rules/api-guidelines.project.md` carries the table with the gap marked.

## What this does not authorise

Treating the move as a breaking change requiring `/v2`. Per
`.claude/rules/protocol.project.md`, changing the status an existing error answers with *is*
breaking — so this one is taken knowingly, before there is a client outside this repository, and
`docs/PROTOCOL.md` changes in the same commit. After that, the rule applies without exception.
