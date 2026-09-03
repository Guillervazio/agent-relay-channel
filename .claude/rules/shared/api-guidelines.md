---
paths:
  - "src/Arc.Hub/**/*.cs"
  - "src/Arc.Core/Models.cs"
  - "src/Arc.Core/ChannelService.cs"
---

# API guidelines — base

Shared across projects. A deviation from any clause here is recorded under `## Deviations` in
`api-guidelines.project.md`, naming the clause it replaces, and that entry wins.

## The envelope

Every response is enveloped: `data` for a success, `data` plus `meta` for a page, `error` for a
failure. The shape itself is a project decision; what is not negotiable is that success and failure
share one, so a client writes one deserialisation path.

* `code` is a **stable PascalCase identifier** a client branches on. It never changes once
  released — [H012](../../../docs/adr/house/H012-an-error-code-is-defined-once-and-a-test-keeps-the-literal.md).
* `message` is human-readable and safe to display: never a stack trace, SQL or an internal
  identifier.
* `details` appears only when the failure has something to say **per field**.
* A detail names the request field a client would put it beside, in the casing the client sent —
  never an internal property path. Every producer of a detail normalises through one helper, or two
  of them will disagree and a client that matches by string will silently fail on the rarer one.

## Route shape

Plural nouns, kebab-case for multi-word segments, and **the method states the action** — never a
verb in the path. An operation that is not one of the verbs is a sub-resource, named as a noun.

**One level of nesting is the limit.** A second level is a filter wearing a path, and it forces a
route to answer for the existence of two resources instead of one.

A new **major version** is a new folder and new controllers. An existing version's controllers are
frozen against breaking changes: that is what a version is for, and editing one in place makes the
number a decoration.

## Which status code

* **422** for a well-formed request a rule refused; **400** only for one that could not be read at
  all — [H013](../../../docs/adr/house/H013-422-for-a-well-formed-request-that-fails-validation.md).
* **409** for a conflict with existing state, including a stale revision.
* **404** where authorisation filters rows —
  [H011](../../../docs/adr/house/H011-404-not-403-when-authorisation-filters-rows.md).
* **500** carries a generic message; the detail is logged, not returned.
* **201** for a create, **with a `Location` header pointing at what was created** — a client that
  has to guess the URL of the thing it just made is a client doing the server's work.
* **200**, or **204** when there is nothing to return.

`PUT` **replaces the resource in full**: it takes every editable field, not the changed ones. That
is why a partial update is not a `PUT`, and why a `PUT` body missing a field is a field being
cleared rather than a field being left alone.

Errors are produced centrally by one exception handler. A controller never builds an error response
by hand and never contains a `try/catch` for error shaping.

## A domain error may carry a datum

A rule that refuses over a number **the client cannot know** puts that number in `details` rather
than only in the sentence. The boundary, because this is the kind of exception that spreads:

* Only when the client **cannot compute** the datum. A rule refusing a value the request carried
  has nothing to add.
* It is what the message cites, not an echo of the input.
* One shape for every datum, so the contract does not grow with each rule that learns to explain
  itself.
* No abstraction until there is a second case.

## Validation

FluentValidation only, no DataAnnotations. Validators sit beside their command in the slice and
validate the **command or query**, never the contract record —
[H005](../../../docs/adr/house/H005-validation-belongs-to-the-command.md). They are executed by the
handler decorator, before the handler body. Validation never lives in a controller.

Shape validation (required, length, range, format) is the validator's. Invariants that must hold
for the aggregate to exist are the domain's. The two overlap by design.

## Controllers

Four steps and nothing else: receive the request record, map it to a command or query, call exactly
one handler, map the result. Forbidden inside one: business logic, validation, database access,
`try/catch` for error shaping, more than one handler call.

Every action takes a `CancellationToken`. Annotate with `[ProducesResponseType]` for **every**
status code the action can return, or the generated document describes something else.

Never expose persistence entities or domain types. Requests and responses are contract records
exposing primitives.

## Paging, sorting, filtering

* Collection endpoints are **always** paged; there is no unbounded list endpoint. Paging metadata
  goes in `meta`, never in headers.
* There is a maximum page size, and exceeding it is a **422** — never a silent clamp. A client
  asking for more has a bug and should hear about it.
* `sortBy` accepts an allow-list per endpoint; an unknown value is a 422, never a silent fallback
  and never raw SQL. **A paged endpoint does not have to offer one** — an allow-list of a single
  column is scaffolding with a query parameter on it. Add it with the second column that earns it.
* **Filters are explicit query parameters.** No generic query language, no OData, no
  `?filter=field~value` grammar. Every rule below assumes each filter was decided one at a time,
  and a query language is precisely the thing that stops being true of.
* **A filter is not an address.** An identifier in the query string that matches nothing yields an
  empty page; a path segment naming a resource that does not exist is a 404. The same identifier
  behaves differently in the two positions, on purpose.
* **Say whether a text filter is exact or partial, and pick for a reason.** A name is half
  remembered, so it is a partial match; an identifier is copied off a document, so it is exact —
  and exact is also the only form an index can serve.
* **Filterable does not mean sortable.** Adding a filter never adds to the sort allow-list.

## Authentication and authorisation

Bearer tokens. Endpoints are protected by default and anonymous access is opt-in per action. **A
route mapped outside the MVC pipeline opts out the same way, explicitly** — forgetting it on a
health probe does not look like an auth bug: the fallback policy answers 401, the orchestrator
reads an unhealthy container, and the symptom is a restart loop.

Authorisation uses **named policies**, never role strings scattered through controllers.

### When a policy is the wrong shape

A policy decides **whether a request proceeds**. It cannot decide **what a request may see**, and an
endpoint whose rows differ per caller needs the second thing: the caller's permissions are read as
a filter instead.

The boundary, because this exception spreads:

* Authentication is still required — "no policy" means the fallback applies, never "no authorize".
* If every caller a policy admits gets the **same** answer, the policy is correct. Ask whether the
  *rows* differ, not whether the check is easier to write in C#.
* A permission is **never** a request parameter. It comes from the token.

## Pipeline order

CORS is registered **before authentication and before HTTPS redirection**. A preflight carries no
token: after authentication it meets the fallback policy's 401, and after HTTPS redirection it gets
a 307 the browser will not follow. Both surface as a CORS error that mentions neither — which is
why a scenario test asserts the preflight is answered rather than trusting the order.

This is a documented property of the ASP.NET Core pipeline, not a decision taken here.

CORS is **not** authorisation and must never be described as such. It is enforced by the browser,
so a script, a service or a mobile app ignores it entirely.
