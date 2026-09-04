# Increment 04 — a seam for the three surfaces

Three surfaces — 1,144 lines across `Arc.Hub/Program.cs`, `Arc.Hub/ArcTools.cs` and
`Arc.Cli/Program.cs` — had **zero** xunit coverage. The 401, the `bad_agent` 422, the 200-versus-202
mapping, the empty mailbox's 204, all seven MCP tools and the CLI's five published exit codes were
exercised only by `scripts/test-all.sh`, which no gate runs.

It could not be fixed by writing tests, because there was nothing to write them against. Both
`Program.cs` files were top-level statements whose first act was reading the environment. Two
increments in a row had shipped a fix with "no unit test, verified by hand" — `ARC_MAX_WAIT` and
the CLI's `--refs`, both in increment 03 — and that is the debt this pays.

**97 tests, from 52.**

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | `HubOptions` and `HubApp.BuildAsync`: the hub takes its configuration instead of reading it | done | `1893126` |
| 2 | The hub, the MCP tools and `EventStream` under test | done | `b6fe02d` |
| 3 | `CliRunner` with its I/O and its transport injected | done | `3429192` `36cc5aa` |
| 4 | The rules stop saying these surfaces cannot be tested | done | this commit |

Verified at close (4 September 2026): `build` **0 warnings, 0 errors**; `test` **97 passed, 0
failed, 0 skipped**; `format --verify-no-changes` clean; `bash scripts/test-all.sh` **four suites
green, 64 checks**.

---

## The refactors were kept separate from the tests, deliberately

Each surface moved in two commits: the seam alone, then the tests it made possible. The rule the
workflow states is that a refactor sharing a commit with a feature makes a failure ambiguous about
which half caused it — and here the refactor's only evidence *is* that nothing changed. Phase 1's
proof is `test-all.sh` passing unmodified against the restructured hub; phase 3's is `smoke-cli.sh`
passing its 15 checks, exit codes included, against the restructured client. A commit that had also
added tests would have muddied both.

## What the shape is, and what it forbids

```csharp
HubApp.BuildAsync(HubOptions hub, Action<IWebHostBuilder>? configureWebHost = null)
CliRunner(TextWriter output, TextWriter error, TextReader input, TimeProvider? time = null)
    .RunAsync(string[] args, HttpMessageHandler? handler = null)
```

Configuration in through the parameter list, never read from ambient state. It is the shape
`ChannelService` already had, so it is not a new pattern — and the second thing it bought was
removing the reason two hub tests could never have run at once.

`Program.cs` on each side is what remains: 68 lines in the hub, 9 in the CLI, all of it reading the
environment, refusing it if it will not work, and running. The rule that came out of this and is now
in `architecture.project.md`: **`Program.cs` is not a place to put a decision**, because a rule that
lands there is a rule no test can reach.

## Two expectations that looked like defects and were not

Both surfaced in `CliRunnerTests`, and both are recorded because the first reading was wrong:

* **The unknown-command help goes to stderr**, not stdout. `Fail` writes there, which is correct —
  a shell redirecting stdout to capture output should not catch a usage error.
* **The request body escapes non-ASCII.** A test looking for `céntimos` in the bytes on the wire
  finds `céntimos`. The stdin test now asserts what the hub reads *after* deserialising, which
  is the thing that actually matters and the thing the escaping does not change.

## Where the boundary between xunit and the smokes now runs

The smokes did not go away, and the appendix now says what each is for, because otherwise the next
check lands in whichever was touched last:

* **`HubEndpointTests`** owns anything the pipeline decides — status codes, headers, refusals.
* **`test-all.sh`** owns what only a real process can show: the published `arc.exe` running,
  Kestrel under a long poll, the observer page's stream over a real connection.

A check that could live in either belongs in xunit, because that is the one a gate runs.

## What is still not covered, named rather than left implied

`/v1/observe/stream` has no xunit test. `HubEndpointTests` asserts routes that answer and return,
and an SSE endpoint answers by not returning; reading it needs a test that takes a bounded prefix of
the response body rather than awaiting the whole of it. `EventStreamTests` covers the queue behind
it and `smoke-ui.sh` drives the real thing, but nothing in the fast gate asserts the two-second
`: ping`. It is in the backlog with that trigger and that shape.

## Rules this made false

* `architecture.project.md` said `Arc.Cli/Program.cs` "is top-level statements with no seam, and
  reaching for one would be a design change, not a test". The design change is what this increment
  was, so the paragraph now names both composition roots and what they do not authorise. Its
  dependency graph gained `Arc.Tests → Arc.Hub`, and its "where a type goes" table pointed at two
  files that no longer hold what it said.
* `testing.project.md` called the empty scenario row "the honest position: the 401 path is
  exercised only by a bash script that no gate runs". It is now covered, and the appendix says how
  the work divides between the suite and the smokes instead.
* `concurrency.project.md` and `api-guidelines.project.md` both pointed at `Arc.Hub/Program.cs` for
  the Kestrel settings and the error middleware — including in `concurrency.project.md`'s `paths:`
  frontmatter, which means the rule would have stopped loading when the file it guards was edited.
* `P007` said `Arc.Hub/Program.cs` calls `InitializeAsync` before serving. `HubApp.BuildAsync` does.
