# Increment 13 — the human is still the transport

The channel makes a turn **wait**. Nothing makes a turn **happen**. A message for an idle agent sits
in its mailbox until somebody opens a console, so every exchange this project has ever had needed a
person to start both sides.

The [demo](../../demo/) had been demonstrating both halves in the same run and nobody had read it
that way: one agent told to block in its mailbox, the other handed a question already written, and
two consoles opened by hand to reach an exchange that then needed nobody at all. The purpose the
README states was met by the channel and not by the system. The person had stopped being the
transport and become the scheduler, which is a smaller job and still a person in the middle of
every message.

[P024](../adr/P024-who-supplies-the-turn.md) settles it: a turn is supplied on the agent's own
machine and never by the hub, and the agent holding the conversation opens the other's turn when it
needs an answer — queue with `wait = 0`, start the other's turn, and only then block on `arc await`.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | P024, the README's half-true sentence, the demo rebuilt around one `empezá`, and the backlog | done | `00457bc` |
| 2 | Close: the architecture clause this decided something for, two stale claims on `CLAUDE.md`, the record and the log | done | this commit |

Verified at close (8 September 2026): `dotnet build` **0 warnings, 0 errors**; `dotnet test`
**136 passed, 0 failed, 0 skipped** — none new, because no code changed; `dotnet format
--verify-no-changes` clean; `dotnet restore --force` clean, no advisory. `scripts/test-all.sh` was
**not** re-run and increment 12's run of it stands, on increment 10's precedent: nothing under
`src/` moved.

Verified by hand, because no test reaches any of it: every relative link in the eight changed files
resolves, checked mechanically rather than by reading; and the `77 files of 122` count on
`CLAUDE.md` was **recounted** against `git ls-files` and the rules' own globs rather than adjusted
by the two files this increment adds.

**Nothing exercised the arrangement itself, and that is the whole of what is unverified.** Opening a
turn needs a second CLI installed and a real turn command, and this increment deliberately produced
neither. What is shipped is the decision, the rules and the instructions the agents read — not a
run. That is now the first row of [backlog.md](../backlog.md)'s findings, with what would make it
due, and it has the same standing the service installation has had since increment 06.

---

## What the plan got wrong

| The plan said | What was true |
|---|---|
| The subject was a guide written in another repository, to be reviewed and adopted into `docs/` | Reviewing it is what surfaced this, and then the guide was not the work. Its §1 asserts that supplying turns is outside this project by design, that assertion was taken as binding for two whole answers, and it is not this repository's: [P001](../adr/P001-long-polling-not-a-broker.md)'s own *what this does not authorise* says it argues about a client that does not exist between turns and declines to make the **transport** a broker. It says nothing about who opens a turn. An outside author's claim about our scope had been standing in for our own record |
| The decision would forbid naming a provider's CLI in this tree | The tree already names providers in prose, in the demo and in the README, and always has. The clause had to be narrowed to the **invocation** — the executable, its flags, its sandbox settings — which is the half a version changes underneath you. Written the first way, the rule would have been false on the day it was committed |
| A decision recorded in `docs/adr/` discharges the reconciliation | It does not, and `reconcile-rules` says so in as many words. `architecture.project.md` names three roles and a table of where a type goes, so a reader asked to add turn supply would have reached for a surface or the hub — the two places P024 forbids. The rule now says supplying a turn is not a role here, and what that does not license |
| `CLAUDE.md` needed one line adding P024 to the settled list | It needed that and a recount. **Half of what is versioned here matches no `paths:` — 77 files of 122** was measured on 6 September 2026 and had drifted before this increment touched anything: it is 84 of 131. The number is now dated and carries the instruction to recount rather than quote, because a measured claim on that page has nothing that re-measures it — which is the same shape as the finding increment 12 left in the backlog about the smoke suites |
| The counterpart's instructions needed a smaller wait | They needed a different *reason*. Both counterpart files opened by telling the agent to block for three minutes; under P024 its turn is opened **because** something is already in its mailbox, so the short wait is a margin and the file has to say which of the two it is. An agent told to wait without being told why will wait again at the end of its turn, and the turn is exactly what it must not spend |
