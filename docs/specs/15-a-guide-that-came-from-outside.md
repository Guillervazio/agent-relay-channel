# Increment 15 — a guide that came from outside

A session in another repository set ARC up in a real project, ran two agents against it, and wrote
down what it hit. That report arrives here as [arc-dev-environment.md](../arc-dev-environment.md),
copied rather than linked, because a document that lives in another repository only in passing is a
document that will be moved or deleted without telling us.

It arrived with four known problems, two more found while reviewing it, and — by the time it was
adopted — two more created by this repository in the meantime.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | The page, the README's structure table, the routing row and the decay entry | done | this commit |

Verified at close (8 September 2026): `dotnet build` **0 warnings, 0 errors**; `dotnet test`
**136 passed, 0 failed, 0 skipped**; `dotnet format --verify-no-changes` clean; `dotnet restore
--force` clean, no advisory. No code changed, so `scripts/test-all.sh` was **not** re-run and
increment 12's run of it stands.

Verified by hand: every relative link in the page and the three edited files resolves, checked
mechanically. And the replacement retry loop in §8 was **run against a stub** rather than reasoned
about — it stops after three calls carrying the right exit code, while the loop it replaces had to
be killed by a timeout.

## What was corrected, and where each correction came from

| Correction | Where it came from |
|---|---|
| The host is declared at the top, and §5 and §9 say which failures are Windows | Review |
| Codex CLI **v0.153.4** is named, with the date it was observed | Review |
| §7's template pruned to what the handshake cannot know — roughly a third of what arrived | Review |
| The self-addressed claim confirmed against `ChannelService` rather than deduced, and the refusal's **second door** added | Review, then checked in the code |
| §8's retry loop no longer spins on any error, only on a timeout | Found while reviewing |
| §6 no longer promises a token is unnecessary and then offers an endpoint that requires one | Found while reviewing |
| §1 no longer asserts that supplying turns is outside this project | [P024](../adr/P024-who-supplies-the-turn.md), increment 13 |
| §6 reframed: opening the other's turn is the default, the loop is the alternative | [P024](../adr/P024-who-supplies-the-turn.md), increment 13 |
| §7 loses "what you know goes in the file" to the channel itself | [P025](../adr/P025-what-earns-a-place-in-the-handshake.md), increment 14 |

Left as it arrived, deliberately: **Option A of §5 is still untested and still says so**, and the
wrong explanation for why git failed is still written out next to the right one, because the wrong
one is far more plausible and somebody will reach it again.

The supervisor script did **not** become a file in `scripts/`. Everything there has this hub as its
subject, nothing in it invokes another project's CLI, and a version-coupled script in that folder
would have no row in `build-and-packages.project.md` and nothing able to re-check it. It stays a
described shape, and under P024 it is no longer the default one anyway.

## The reconciliation found nothing to change

No rule became false. The page is prose in `docs/`, no clause governs what may live there, and it
adds no pattern. What it does add is a **decay property** — its commands are facts about one host
and one version of somebody else's CLI — so that is a finding in [backlog.md](../backlog.md) with
its trigger, and `CLAUDE.md`'s routing table gained the row that says so before anybody edits a
command in it.

---

## What the plan got wrong

| The plan said | What was true |
|---|---|
| Adopt it with the four corrections from the review | Nine. Two of them did not exist when the review was written: increments 13 and 14 landed in between and contradicted the document's opening section and emptied part of its template. **Reviewing a document and adopting it are separated by whatever happens in between**, and here that was two increments that changed the answer |
| Either the page declares its host, or it gets the Linux and macOS equivalents | The second was never available. Nobody has run them, and writing them would be inventing behaviour — which is the defect §5 was already corrected for once, and the defect P025's last clause exists to name. Declaring the host is not the cheaper option, it is the only honest one |
| The busy loop is a wording fix | `until cmd; do :; done` and a loop that branches on the exit code read almost the same and differ in bash, so the replacement was run against a stub that returns two timeouts and then an error. That is three lines of shell in a document nobody compiles, which is exactly the kind of thing that ships wrong |
| §7's template needs pruning | Pruning was not the operation by the time it happened. One of its rules had **left** for the handshake in increment 14, so the section had to point at the channel and restate nothing, and what remains is about a third of what arrived. A section that says "do not repeat what the handshake says" and then repeats it was the original defect; the fix is that it now names what arrives on its own and stops |
| Copying rather than linking is a placement detail | It is what makes the decay entry necessary. A link would have gone stale invisibly somewhere else; a copy goes stale here, where the backlog can carry a trigger for it. The instruction to copy was the right one and it has a cost, and the cost is now written down rather than absorbed |
