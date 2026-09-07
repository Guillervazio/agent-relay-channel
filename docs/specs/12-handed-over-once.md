# Increment 12 — handed over once

`InboxAsync` read the mailbox with one call and marked what it had read with a second. Between the
two fitted another poll by the same agent: both read the same pending rows, and **both HTTP
responses carried the message**, although only one `UPDATE` marked it.

The database was coherent throughout. That is why this survived nine increments — what broke was
the answer, not the row, and every assertion anybody had written looked at the row. The same call
also reported `status: "pending"` for a row it had just set to `delivered`.

Both were one change, as [backlog.md](../backlog.md) had said since increment 09: put the read and
the marking in one transaction.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | `ClaimInboxAsync`, the caller, the tests and `PROTOCOL.md` | done | `56e4c3d` |
| 2 | Close: the record, the log, and the backlog entry that is now half its size | done | this commit |

Verified at close (7 September 2026): `dotnet build` **0 warnings, 0 errors**; `dotnet test`
**136 passed, 0 failed, 0 skipped** — three new; `dotnet format --verify-no-changes` clean;
`dotnet restore --force` clean, no advisory; `bash scripts/test-all.sh` **four suites green, 114
checks** (45 REST, 28 CLI, 30 MCP, 11 UI).

**Verified by hand against a running hub on `:8809`, because no suite reaches it.** The four smoke
suites passed *unchanged* over a wire that had changed, which is the finding below and the reason
this section is not a formality. A note posted to `codex-pc2` came back from the first read with
`"status": "delivered"`; the default mailbox then answered `204`; `?replay=60` returned the same
notice, still `delivered`, and marked nothing. Then three notes and **four simultaneous polls**:
one poll received all three, the other three received `204`, three delivered in total and three
distinct. That last one is the defect, observed absent.

---

## What the plan got wrong

| The plan said | What was true |
|---|---|
| One store method replaces the pair at the call site | It replaces the pair, and then `GetInboxAsync` and `MarkDeliveredAsync` have no caller left outside the tests. Deleting them was not in the plan and is the part the rules demanded: a store method kept alive by its own tests is what the architecture appendix refuses. The tests lost nothing — a row becomes delivered in a test now exactly as it does in the hub |
| Return the messages; the caller derives which ones it just delivered | It cannot. A message brought back by `?replay=` is already `delivered`, so deriving the set from the returned status announces a delivery that did not happen on this call. The operation returns the messages **and** the ids it claimed, and the observer event uses the second |
| The concurrency test is the proof | It is the exercise, not the proof, and its own comment says so: eight polls over five messages contend in practice but nothing guarantees the interleaving. What settles the defect is that the gap no longer exists outside the store — there is no way to read the mailbox without claiming it. The by-hand run against a real hub is the closest thing to evidence, and it is four processes rather than four tasks |
| The wire changes, so this needs care about breaking | It needed less care than expected and for a reason worth writing down: `PROTOCOL.md` had said `pending → delivered (when read from the mailbox)` since it was written. The **code** was the half that was wrong, which `protocol.project.md` already covers — the document did not move, it was arrived at |

## What was decided

[P023](../adr/P023-the-mailbox-is-claimed-not-read.md). One operation inside one transaction,
returning what this caller actually took; the returned status is the row's; the two old methods
deleted rather than kept. It is [H007](../adr/house/H007-optimistic-concurrency-where-an-update-derives-from-a-read.md)'s
shape, the one `AddResponseAsync` already used — the decision to hand a message over came from a
row that was read, so the `UPDATE`'s `WHERE` repeats it and the affected-row count says who won.

The record's boundaries are the load-bearing part: this is not acknowledgement, the wait must never
move inside the transaction, and "one transaction" is not a licence anywhere a read decides nothing.

## The rules it made false

None, and the check was not a formality — five clauses were read against the change and each holds.
`persistence.project.md` already required exactly this shape for a write deriving from a read;
`architecture.project.md` is what forced the two deletions rather than permitting them;
`api-guidelines.project.md`'s paging clause is untouched, because nothing about *how much* of the
mailbox comes back changed; `protocol.project.md` is what said the code moves rather than the
document; `concurrency.project.md` describes the waiter and the long poll, and the wait still sits
between two claims and never inside one.

`docs/backlog.md` lost one finding and half of another. What remains of the second is the half that
is not a defect in the code at all: nobody tells the hub the message arrived, and changing that is
a change to [P001](../adr/P001-long-polling-not-a-broker.md).

## What this increment did not fix, deliberately

**The message is still marked before the client has it.** A response lost in transit still empties
the default mailbox, and `?replay=N` is still the way back rather than a repair. Fixing it means a
client that acknowledges, which is the broker this channel decided not to be.

**Nothing about `MessageStatus.Expired`**, still published and never produced, still waiting for a
`/v2`.

## A finding this increment produced

**The four smoke suites passed unchanged over a changed wire.** `status` on a first inbox read went
from `pending` to `delivered` and 114 checks noticed nothing, because not one of them asserts the
status of a message it just read. That is the same shape as every defect this repository has found
twice: a green suite over behaviour nobody had written an assertion for. It is in
[backlog.md](../backlog.md).

**And a test that fails rarely now has a name.** `ArcToolsTests.Un_hilo_devuelve_la_conversacion_entera`
failed in the first of three consecutive full-suite runs here, and did not reproduce in the eight
runs after it or in six runs of `ArcToolsTests` alone. Increment 08's entry existed precisely so
the second occurrence would carry the name the first one lost. It does. The cause does not follow
from it, and `WaiterRegistryTests` — the standing suspect since 08 — is not it.
