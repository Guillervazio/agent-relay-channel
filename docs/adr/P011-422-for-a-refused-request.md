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

Seven codes answer 422: `self_addressed`, `empty_body`, `body_too_large`, `bad_recipient`,
`bad_agent`, `invalid_refs` and `invalid_wait`. `invalid_json` stays 400 and is the case H013
exists to distinguish — one code, for the one situation where nothing could be read.

`bad_agent` was the open question and went to 422. A malformed `X-ARC-Agent` header is arguably
framing rather than content, but it is `AgentNamePattern` refusing a value, and that same pattern
refusing that same shape inside the body is `bad_recipient`. One validator answering two different
statuses depending on where the value travelled is the incoherence this record removes.

`invalid_wait` is new, and it replaces a silent clamp: `Math.Clamp` gave a caller asking for 600
seconds a 300-second wait that came back looking like an ordinary timeout.

## What this does not authorise

Treating the move as a breaking change requiring `/v2`. Per
`.claude/rules/protocol.project.md`, changing the status an existing error answers with *is*
breaking — so this one is taken knowingly, before there is a client outside this repository, and
`docs/PROTOCOL.md` changes in the same commit. After that, the rule applies without exception.
