# Increment 03 — pay what the backlog says is due

Five entries whose written trigger was **now**. Increment 02 found nine defects and fixed none of
them, by scope; this is the increment that pays the four the backlog judged due on merit, plus the
one piece of housekeeping in the same state.

Nothing here is a feature. The wire did not move, `PROTOCOL.md` did not change, and the only
externally visible difference is that a hub with a bad `ARC_MAX_WAIT` now refuses to start instead
of running useless.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | `PRAGMA synchronous` moves into `OpenAsync`, where every pooled connection meets it | done | `d823648` |
| 2 | `WaiterRegistry` stops losing a registration to an eviction | done | `060b883` |
| 3 | `ARC_MAX_WAIT` is refused at startup when it would brick the channel | done | `51b56f9` |
| 4 | A malformed `--refs` exits `2` instead of sending the message without it | done | `36de102` |
| 5 | The denylist names the two commands it was written to stop | done | `8b4e157` |

Verified at close (4 September 2026): `build` **0 warnings, 0 errors**; `test` **52 passed**, from
49; `format --verify-no-changes` clean; `bash scripts/test-all.sh` **four suites green, 64 checks**
— 24 REST, 15 CLI, 14 MCP, 11 UI.

---

## Every fix was confirmed by watching it fail first

Two of these are the kind of defect that a test can be written *around* rather than *for*, so each
was checked by putting the fault back:

* **The pragma.** Removing the line from `OpenAsync` makes `ConnectionPragmaTests` fail on the
  second connection, not the first — which is exactly the shape of the original defect, since the
  first connection out of the pool is often the initialised handle and would have passed either way.
  A test that asked once would have been green against the broken code.
* **The race.** A `Thread.Sleep(0)` between the `GetOrAdd` and the insert widens the window to
  certainty, and the 500-round test fails immediately. Without that check the test proves nothing:
  a race that never triggers and a race that cannot trigger look identical from a green suite.

## What the backlog got wrong about its own entries

| The entry said | What was actually true |
|---|---|
| The waiter race is fixed "by retrying the registration when the dictionary it got has been evicted" | Retrying leaves a residual window: the eviction can land after the re-check and before the caller has its `Waiter`. One lock over `Register`, `Unregister` and `Signal`'s lookup closes the class of fault instead of narrowing it, and at a few dozen messages a day its contention is not measurable |
| The denylist entry is "a pattern nobody has seen match anything", to be replaced with a wildcard form | Wildcards were never the problem — they are documented to work at any position. The **literal space** was: `rm * demo/arc.db*` demands a segment between `rm` and the path, so `rm -f demo/arc.db` would have matched and `rm demo/arc.db` never could. The pattern also only named the demo copy, not the database the hub writes beside its executable |
| `ARC_MAX_WAIT` needs "refusing a non-positive value at startup" | It also needs a ceiling. `KeepAliveTimeout` is derived as `ARC_MAX_WAIT + 60`, so `int.MaxValue` overflows the addition before Kestrel sees it — an adjacent defect nothing had named, found by touching the line, and one `return 1` away from the fix already being written |

## The seam question, answered twice and differently

Two phases needed to observe something no public surface reveals, and they were not given the same
answer:

* **The store's pragmas** got `InternalsVisibleTo` on `Arc.Core`. The precedent already existed —
  `Arc.Tests → Arc.Cli` opens `ExitCodes` the same way — so this is not a new pattern, and the bar
  it cleared is recorded in `architecture.project.md`: the pragmas are a decision this repository
  writes down, nothing public reveals them, and a defect in them had already survived a whole
  increment unseen.
* **The CLI's `--refs` fix** got no unit test at all, and neither did `ARC_MAX_WAIT`. Both live in
  top-level statements reading process state, where the seam is a design change rather than a test
  arrangement. `smoke-cli.sh` covers the first — asserting the exit code *and* that the mailbox
  count did not move, because the fix is worth nothing if the message still goes — and the second
  was checked by hand: `-5`, `0`, `abc` and `999999999` each exit 1 naming the value, and `45`
  reaches `/healthz` as `max_wait_seconds` intact.

That asymmetry is the increment's honest position, not an oversight: widening visibility to observe
what the design hides is defensible; manufacturing a composition root mid-increment to avoid running
a shell script is not. Increment 04 is where that seam gets built deliberately.

## Rules this made false

* `persistence.project.md` said one of its three connection settings "is not actually in force, and
  saying so is the point of writing it down". It now says where each pragma is emitted and why the
  two differ — and what that does not authorise, which is adding a per-connection pragma to
  `InitializeAsync`, where it looks right in the diff and is silent at runtime.
* `concurrency.project.md` described `WaiterRegistry` as a `ConcurrentDictionary`. It now says that
  each dictionary being concurrent is what made the *sequence* over them look safe, and that the
  lock is affordable here because of this channel's volume — not a licence to widen a lock on a hot
  path elsewhere.
* `architecture.project.md` named `InternalsVisibleTo` as a one-off for `ExitCodes`. There are two
  now, so it states the bar the second one cleared.
