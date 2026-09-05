# P017 — `refs` is any JSON value, and the document is what moved

## Context

`PROTOCOL.md` called `refs` "a free-form JSON object". Nothing anywhere checked that it was an
object: `ChannelService` takes a `JsonElement?` and stores its raw text, and the only validation in
the codebase is `ArcTools.ParseRefs`, which parses and rejects what is not JSON at all.

So `invalid_refs` was published, frozen by `ArcErrorsTests`, and thrown from one site that no suite
reached — and it promised a check about *shape* that nothing performed. `refs` was also unbounded
while `body` is held to 256 KB, which is the opposite of what the field is for: it exists so that a
commit and a path travel instead of the file.

## Decision

**`refs` is any JSON value**, stored and returned verbatim. An object is the convention and what
every example shows; nothing rejects an array, a string or a number, and `PROTOCOL.md` now says so.

**`invalid_refs` keeps its code and its 422 and drops the claim about shape.** It means `refs`
could not be read as JSON at all.

**It is reachable over MCP alone, and that is documented rather than repaired.** MCP takes `refs`
as a string argument the hub parses on its own, so it can fail by itself. Over REST `refs` travels
inside the request body, so malformed `refs` are a malformed body and `invalid_json` 400 is the
right answer. The CLI parses `--refs` before it sends anything and exits 2 without reaching the hub.

**`refs` gets no size limit of its own.** What caps it is the request as a whole, which Kestrel
holds to `MaxBodyBytes * 2`, and that number is now in the document.

## Consequences

Tightening was the alternative, and it is the one the finding proposed first: check `ValueKind` in
`ChannelService` so all three surfaces share it. It fails the test
[protocol.project.md](../../.claude/rules/protocol.project.md) sets for a new refusal — name the
client that breaks and show it could only be an abusive one. A client sending
`refs: ["src/x.cs"]` is not abusive. It read an example and inferred a rule nobody was enforcing,
which makes it exactly the honest client the exception is written to protect. The same argument
rules out a cap on `refs`: a number nobody has ever been given cannot be one they were breaking.

What this buys is smaller than a validation and is worth having: the document and the code now
agree, and a client can be written against either one.

The MCP tool descriptions still say "objeto JSON, por ejemplo {...}". That is not a disagreement.
A `Description` says *what to send*, and the convention is what to send; the contract says what is
*refused*, and nothing here is.

## What this does not authorise

**Reading this as "the document yields when the code disagrees".** It yielded here because the code
was the wider of the two and had been for every released version, so no client could have relied on
the refusal that never happened. Where the document is the narrower half and clients have been
obeying it, the repair is the other way around.

**Treating an unbounded field as acceptable in general.** `refs` is unbounded because capping it
now would refuse what clients already send, not because size does not matter. If a `refs` large
enough to hurt ever arrives, the answer is a `/v2` with the limit stated, and the reason this
record exists is so that change is not mistaken for a bugfix.

**Any claim that `refs` is validated.** It is not, in any surface, beyond being JSON. Code that
reads `refs` back must handle a value that is not an object, and
[MessageStore](../../src/Arc.Core/MessageStore.cs) reparses whatever was stored.
