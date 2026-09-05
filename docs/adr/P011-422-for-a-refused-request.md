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
`docs/PROTOCOL.md` changes in the same commit.

**"Before there is an outside client" is spent, and it is not a door that reopens.** It was true
once, in this record, and quoting it again is quoting a fact about a repository that no longer
holds. `.claude/rules/protocol.project.md` is the authority on what is breaking and on the single
exception it allows — a refusal no honest client could hit, which is how increment 07 narrowed two
read routes without a `/v2`
([P016](P016-a-message-is-read-by-its-two-ends.md)). **This record's own change would not qualify
under it**: a status moving from 400 to 422 is one an honest client hits on its first mistake.
Two different escape hatches, one of them closed.
