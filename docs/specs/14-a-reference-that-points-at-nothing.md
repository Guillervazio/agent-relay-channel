# Increment 14 — a reference that points at nothing

The handshake tells an agent to send **references and not content**, because the other side has its
own clone of the repository. It never said the content has to actually be in the file the reference
names.

A session in another repository hit what follows from that. The agent answered richly in the
channel — figures, caveats, a verdict — and wrote a thinner report to disk. **The channel answer
looked complete, so the gap was invisible until somebody opened the file.** The reference was
honest and pointed at less than the reply had promised.

That report put the rule in a per-project `AGENTS.md`, which is the copy
[P014](../adr/P014-the-channel-explains-itself-in-the-handshake.md) exists to prevent — and wrong
for a reason more specific than drift: the failure is not a property of that project. It is
available to anybody who follows the instruction this channel already gives.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | The clause in `ArcInstructions` and `docs/AGENTS.md`, [P025](../adr/P025-what-earns-a-place-in-the-handshake.md), and the assertion | done | `5aa3400` |
| 2 | Close: the two rules that said what may **not** go there and not what earns a place, the copy nothing keeps in step, the record and the log | done | this commit |

Verified at close (8 September 2026): `dotnet build` **0 warnings, 0 errors**; `dotnet test`
**136 passed, 0 failed, 0 skipped**; `dotnet format --verify-no-changes` clean; `dotnet restore
--force` clean, no advisory. `scripts/test-all.sh` was **not** re-run: the change is one constant's
text and no surface behaviour moved, and none of the four smokes reads the handshake.

**The assertion was proved rather than trusted.** `ArcInstructions.cs` was stashed and the test run
alone: it failed with `Not found: "no sólo al canal"`, then the change was restored and it passed.
That step is not ceremony here — the test count did not move, 136 before and 136 after, and a green
suite over an assertion nobody watched fail is the exact shape of two findings already in
[backlog.md](../backlog.md).

---

## What the plan got wrong

| The plan said | What was true |
|---|---|
| Add the sentence to `ArcInstructions` and mirror it in `docs/AGENTS.md` | The sentence is the cheap half. P014 said what may **not** go in that text — an enforced rule, anything a tool's `Description` already says — and never what **earns** a place, because nothing had asked to be added since increment 05. Without that, the second candidate has nothing to be measured against and the text grows by whoever argues best. P025 is four questions, and it is the part of this increment worth keeping |
| The clause is a good rule, so it belongs | "Good rule" is not the test and would admit almost anything. What carried it is narrower: it **completes** a clause already in the text rather than opening a topic. You send a reference because the content is there, so if it is not there the reference points at nothing. The second half was missing from a sentence that had been shipping since increment 05 |
| A new test | It is a fourth assertion on the test that already asserts three, because it is the same logical fact: the handshake arrived and it is the channel's. So **the count did not move**, which is why the failing run was staged deliberately instead of reading the diff and believing it |
| `docs/AGENTS.md` is a mechanical mirror | Writing the second copy is what showed that **nothing keeps the two in step**. P014 granted that one exception and no test, no script and no check compares them; a mechanical one is not even obvious, since one copy is Spanish prose for a model and the other English prose for a person. It is now a finding with what would make it due, rather than a thing this increment quietly relied on |
| An outside report's conclusions come as a set | They do not, and this increment took one and rejected another from the same document. Its observation of a real failure is evidence and is used here. Its assertion that supplying turns was outside this project by design was not binding and was wrong, which cost increment 13. P025's last clause names the distinction so the next report is read that way from the start |
