# Current work

## Increment 08 — promises the code did not keep

Two published promises that nothing enforced, and that had been sitting in
[backlog.md](backlog.md) waiting for the same thing: somebody to decide what the contract actually
says.

`PROTOCOL.md` calls `refs` "a free-form JSON object" and nothing anywhere checks that it is an
object. The one code that would say so, `invalid_refs`, is published, frozen by a test, and thrown
from a single site no suite reaches — and REST cannot emit it at all, because there `refs` travels
inside the request body and a malformed one fails the whole parse as `invalid_json` 400.

`AskAsync` refuses a message to oneself with `self_addressed`; `NoteAsync` has no such check. Two
sibling operations disagreed with no comment, rule or record saying why, so one of the two was
wrong and nothing said which.

Neither is fixed by tightening. **A new refusal on a published route is breaking**, and
[protocol.project.md](../.claude/rules/protocol.project.md) sets the test: name the client that
breaks and show it could only be an abusive one. A client sending `refs: ["a.cs"]` is not abusive,
and neither is an agent leaving itself a question for its next turn. Both halves therefore close by
**widening the contract to what the code already does**, and by writing down the part that stays
refused and why it is not the same case.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | `refs` is any JSON value, and `invalid_refs` says which surface can emit it | done | this commit |
| 2 | An agent may queue a question to itself, and may not wait on it | pending | |
| 3 | Close: the records, the spec, and what the backlog no longer owes | pending | |

**Every phase is closed in the same commit that updates its row.** The increment is closed by
moving the narrative to [specs/](specs/) and leaving this file as it was.

---

## Last verified before this increment

(5 September 2026, at the close of increment 07)

* `dotnet build` — **0 warnings, 0 errors**, `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on
* `dotnet test` — **107 passed, 0 failed, 0 skipped**, 1 project
* `dotnet format --verify-no-changes` — clean
* `dotnet restore --force` — clean, no advisory
* `bash scripts/test-all.sh` — **four suites green, 76 checks** (29 REST, 18 CLI, 18 MCP, 11 UI)

Everything increment 06 verified about hosting — the container, the SDK image, Windows PowerShell
5.1, the installer's reachable half — is unaffected by this increment, which changes what the
channel accepts and touches nothing about how the hub is started. Its record stands in
[specs/06-a-machine-that-is-not-this-one.md](specs/06-a-machine-that-is-not-this-one.md).
