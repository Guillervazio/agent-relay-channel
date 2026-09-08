# ARC in a development environment

An optional guide to leaving two agents working without a person between them. The
[README](../README.md) says how to install the hub and what the commands do, and stops where the
channel stops. This covers what sits around it: the working trees, who is allowed to commit, and
what each agent needs in front of it.

Nothing here is required to use ARC. It is required to leave it running.

---

## Where this came from, and what it is true of

**This is somebody else's field report, adopted.** It was written by a session that set ARC up in
a real project outside this repository and recorded what it hit. Every failure listed below was
met, not imagined, and that is the whole reason it is worth having — but it was met **in one
place, on one machine, against one version of somebody else's tools**.

| | What it was |
|---|---|
| Host | Windows 11, Windows PowerShell 5.1 |
| The second agent's CLI | **Codex CLI v0.153.4**, observed 7 September 2026 |
| Topology | Both agents on one machine, hub on loopback |

**Read every flag name, configuration key and sandbox behaviour below as a fact about that
version.** Codex CLI belongs to another project and changes without asking us. Where a command
here disagrees with the one in front of you, the one in front of you is right.

The **host** matters as much. This repository is not Windows — the
[Dockerfile](../Dockerfile) exists to say so and CI runs on `ubuntu-latest` — but a large part of
what follows is. Sections 5 and 9 are marked where they are Windows-only, and their Linux and
macOS equivalents are **not written here**, because nobody has run them. Inventing them would
repeat the mistake this document has already made once, described next.

**Two things it asserted have since been settled differently, in this repository's own records.**
They are corrected in place below rather than left to be discovered:

* Its opening section argued that starting a turn is not ARC's job, by design. That was an outside
  author's claim about this project's scope and it is not ours —
  [P024](adr/P024-who-supplies-the-turn.md) settles who supplies a turn, and the answer is not
  "nobody here".
* Its rules-for-agents template repeated things the handshake already carries, and included one
  rule that turned out to belong to the channel rather than to any project. That one is now in the
  handshake itself — [P025](adr/P025-what-earns-a-place-in-the-handshake.md).

One thing it got wrong and had already corrected itself, kept because the wrong explanation is far
more plausible than the right one: see [§5](#5-who-commits).

---

## 1. The problem this solves

The README states the constraint everything below follows from:

> A command-line agent **is not a server**: it only exists for the duration of its turn. It cannot
> hold an open subscription, nor wake up for an event that arrives while it is idle.

ARC solves one half. While an agent is inside a turn it can block: `arc ask --wait 180` holds the
request open on the hub, and the answer wakes it instantly. That is real, and it is what a shared
markdown file could never do.

**The other half is starting a turn at all**, and it is not the hub's — not because it is beneath
the project, but because a hub on one machine cannot start a process on another. It is settled in
[P024](adr/P024-who-supplies-the-turn.md): a turn is supplied on the agent's own machine, and the
agent holding the conversation opens the other's turn when it needs an answer.

If nothing does that, you become a message relay. You paste "check your inbox" into one terminal,
wait, read the answer, paste the next instruction into the other. The channel works perfectly and
the experience is worse than not having it.

| | Who provides it |
|---|---|
| The **wait** — blocking until an answer arrives | ARC. Already solved |
| The **turn** — existing at all, so you can wait | The lead agent, on its own machine — [P024](adr/P024-who-supplies-the-turn.md) |

[demo/](../demo/) is the worked example of the second row, and the shortest way to see the shape.

---

## 2. Topology: you do not need two machines

The README describes two PCs because that is the interesting deployment. To *start*, one machine is
strictly easier: one agent in one terminal, the other's turns opened from it, both against a hub on
loopback. No firewall rule, no network profile classification, no service installation.

```
Terminal 1: the lead   ──┐
                         ├─►  arc-hub  (container, 127.0.0.1:8765)  ──►  arc.db (volume)
(turns it opens)       ──┘
```

Publish the container port on loopback specifically:

```bash
docker run -d --name arc-hub --restart unless-stopped \
  -p 127.0.0.1:8765:8765 -v arc-data:/data -e ARC_TOKEN="$TOKEN" arc-hub
```

`-p 127.0.0.1:8765:8765` rather than `-p 8765:8765`. The second form exposes the hub on every
interface, which on a laptop frequently means a café's Wi-Fi. The channel carries instructions
between agents; it has no business listening there.

The database lives in the `arc-data` volume, so recreating the container does not take the mailbox
with it. But `ARC_TOKEN` lives *in the container*: `docker rm` and re-run without the same token,
and every agent starts getting `401`.

---

## 3. Setup

### 3.1 Generate a token, and check that you generated one

**Windows PowerShell 5.1 only.** It fails *silently*, which is why it is worth naming:

```powershell
# WRONG on PowerShell 5.1 — the method does not exist, $bytes stays all zeros,
# and you get a valid-looking token of "AAAAAAAA..." with no error you will notice.
[System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)

# Right
$rng = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
$bytes = New-Object byte[] 32
$rng.GetBytes($bytes); $rng.Dispose()
$token = ([Convert]::ToBase64String($bytes)) -replace '\+','-' -replace '/','_' -replace '=',''
if ($token -match '^A{20,}$') { throw "Token generation failed silently: $token" }
```

The guard on the last line is not paranoia. The failure produces a token that is the right length,
base64-shaped, and completely predictable.

On PowerShell 7 and elsewhere, `RandomNumberGenerator::Fill` exists and this trap does not.
[scripts/ArcHost.ps1](../scripts/ArcHost.ps1) already does this correctly if you are starting the
hub from this repository.

### 3.2 Identity per agent

Each agent needs a different `ARC_AGENT`. It is the key of the wait registry, so it must match
`^[a-z0-9][a-z0-9._-]{0,63}$` — lowercase, no spaces. One capital letter and the hub answers
`422 bad_agent`.

Name them by **role** rather than by machine, unless the machine is the distinguishing fact. On a
single host `claude-pc1` and `codex-pc1` read strangely; `claude-lead` and `codex-research` say
what each one does.

```powershell
[Environment]::SetEnvironmentVariable('ARC_URL',   'http://127.0.0.1:8765', 'User')
[Environment]::SetEnvironmentVariable('ARC_TOKEN', $token,                  'User')
[Environment]::SetEnvironmentVariable('ARC_AGENT', 'claude-lead',           'User')
```

### 3.3 Register the MCP server once per user, not per repository

```bash
claude mcp add --scope user --transport http arc http://127.0.0.1:8765/mcp \
  --header "X-ARC-Agent: claude-lead" \
  --header "X-ARC-Token: <token>"
```

Codex, in `~/.codex/config.toml`:

```toml
[mcp_servers.arc]
url = "http://127.0.0.1:8765/mcp"

[mcp_servers.arc.http_headers]
"X-ARC-Agent" = "codex-research"
"X-ARC-Token" = "<token>"
```

**Check for a stale token before anything else.** If you ever ran the `demo/`, that file already
contains an `arc` block with the demo's token and will keep it. The symptom is `401` from an agent
whose configuration file looks perfectly correct.

Verify by speaking the protocol, not by reading the file back:

```bash
curl -s -X POST http://127.0.0.1:8765/mcp \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "X-ARC-Agent: codex-research" -H "X-ARC-Token: $TOKEN" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"check","version":"1.0"}}}'
```

A `200` carrying `serverInfo` and `instructions` means that agent's credentials work and the
handshake will put the channel's own rules in front of its model.

### 3.4 Prove the cycle against yourself

An agent may queue work for itself and may not block waiting on it —
[P018](adr/P018-an-agent-may-queue-work-for-itself.md). That is enough for a complete end-to-end
test with a single agent, as long as the `ask` does not wait:

```bash
arc health                                                              # 0
arc ask --to claude-lead --subject "probe" --body-file q.md --wait 0    # 3, request stays alive
arc inbox                                                               # 0, shows it
arc respond req_… --body-file a.md                                      # 0
```

Exit code `3` on the `ask` is correct and not a failure: there is no answer *yet*.

`--wait 0` is not incidental. A request to yourself with a real wait is refused with
`self_addressed` 422, because the only party who could answer is the one blocked waiting. **The
refusal lives at both doors**: queueing with `wait = 0` and then blocking with
`arc await --wait 300` is refused too, so there is no two-call way around it.

Two things about the probe worth knowing before you re-run it. Reading a mailbox **claims** what it
finds ([P023](adr/P023-the-mailbox-is-claimed-not-read.md)), so the second `arc inbox` answers
`204` and exits `4` — an empty mailbox, not a broken probe. `arc inbox --replay 60` is how you look
again.

---

## 4. Working trees: give each agent a clone, never a worktree

A `git worktree` is the elegant way to give a second agent a second branch. **It does not work for
a sandboxed agent, and the failure is confusing rather than explicit.**

In a worktree, `.git` is not a directory. It is a file containing a pointer:

```
gitdir: C:/…/main-repo/.git/worktrees/the-worktree
```

Every git write from that folder therefore writes into *another* folder. An agent confined to its
own working directory cannot commit, and depending on the sandbox you will see a permission error,
a hang, or — as we did — a report that "the directory became inaccessible".

Use a plain clone. Its `.git` is a real directory, self-contained, and everything the agent needs is
inside the folder it was given:

```bash
git clone --branch <branch> <remote> ../agent-b-clone
```

**And there must be a remote.** ARC's rule is to send references and not content — "branch X,
commit Y" rather than the file. Two agents with no shared remote have nothing to reference, and the
channel degrades into pasting file contents at each other, which is the shared markdown file ARC
replaced. A shared remote is a precondition, not a nicety.

---

## 5. Who commits

> **Windows.** This whole section is about a Windows sandbox running under a second user account.
> Nobody has run the equivalent on Linux or macOS, so nothing here is claimed about them.

An agent under Codex's sandbox could not commit. The first explanation offered — its own report —
was that `git commit` died creating `.git/index.lock`, which reads like `workspace-write` excluding
`.git`. **That explanation is wrong, and it is worth spelling out how, because the wrong one is far
more plausible than the right one.**

Probing it directly, with `git` invoked rather than a commit attempted:

```
$ git rev-parse --git-dir
fatal: detected dubious ownership in repository at 'C:/…/agent-b-clone'
'C:/…/agent-b-clone' is owned by:
        'S-1-5-21-…-1001'
but the current user is:
        'S-1-5-21-…-1006'
```

**The sandbox executes as a different Windows user than the one that owns the clone.** With
`[windows] sandbox = "elevated"` in `~/.codex/config.toml`, commands run under a restricted token
with its own SID. Git's `safe.directory` protection then refuses to treat the directory as a
repository at all, and every later error — `--local can only be used inside a git repository` — is
a consequence of that refusal rather than an independent finding.

So the constraint is not "`.git` is read-only". It is "**git does not work here**", for a reason
that has nothing to do with ARC, with sandboxes writing files, or with which directory is writable.
The agent writes files in its working directory perfectly well.

### What follows

**Option A — make git usable inside the sandbox. Untested.** The ownership exception has to exist
for the *sandbox's* identity rather than yours, so `git config --global --add safe.directory …` run
as yourself does not help. Nobody has tried the version that would; if you take this path, verify
it rather than assuming it.

**Option B — the writing agent does not commit; the integrating agent does.** This is what was run,
and the probe above makes it a better default than it first appeared:

- It needs no configuration at all, and nothing about the sandbox has to be negotiated away.
- The reviewing agent is already deciding whether the work is finished. Committing what it has
  accepted is the same act, not an extra one.
- It is indifferent to *why* the agent cannot commit — ownership, policy, or a future change in
  another project's CLI.

Under Option B the deliverable is a **file written and an answer sent**. Committing, merging and
pushing belong to the integrator.

### A second, separate restriction

In the same probe, this was refused outright:

```
powershell.exe -Command "Remove-Item -LiteralPath 'probe.tmp' -Force" → rejected: blocked by policy
```

while `powershell.exe -NoProfile -Command 'git rev-parse --git-dir'` in the very same configuration
ran fine. The policy is not blocking the shell; it is blocking **deletion**. An agent that writes a
file it later wants to remove will get stuck and may retry the write-then-delete cycle in a loop.
Tell it not to create temporary files.

---

## 6. Supplying turns

[P024](adr/P024-who-supplies-the-turn.md) settles this, and the shape it chose is the cheap one.

### The default: the lead opens the other's turn

Three steps, and **the order is the whole of it**:

1. **Queue without waiting** — `arc ask … --wait 0`. You get a `request_id` and the request sits in
   the other agent's mailbox. Exit code `3` here is correct.
2. **Open the other's turn**, in the background, with whatever command gives that agent exactly one
   non-interactive turn on this machine.
3. **Now block** — `arc await <request_id> --wait 300`.

Blocking first is the mistake worth naming: `arc ask --wait 300` as the first contact parks the
lead before anything has opened a turn, and the wait can only end by expiring.

Nothing runs while nothing is happening, and there is nothing to remember to stop. The turn command
is configuration for your machine — [demo/README.md](../demo/README.md) has the shape and says why
this repository ships a placeholder rather than a command.

### The alternative: a loop, when both sides must initiate

The default has one real narrowing: **only the lead can start an exchange.** An agent whose turn was
opened to answer a question can answer it, and cannot raise something an hour later on its own.
Where both sides genuinely need to initiate, the shape is a loop per agent — a process that opens a
turn, lets the agent block in `arc_inbox` for up to `ARC_MAX_WAIT`, and opens another when that one
ends.

It costs a turn per interval whether or not anything arrives, and it has to be stopped when the
increment closes. Keep its stop file and its log **outside** the clone, or they become untracked
files the agent sees in every `git status` and eventually commits.

**Whatever supervises, it must not read the mailbox.** Reading one claims what it finds
([P023](adr/P023-the-mailbox-is-claimed-not-read.md)), so a watcher would be taking delivery of
messages on behalf of an agent that has not seen them. To react to traffic rather than poll, watch
`/v1/observe/stream`, which marks nothing.

That endpoint **does require `X-ARC-Token`** — the hub's authentication runs before the observer
exemption, and what the exemption waives is only the `X-ARC-Agent` header. A watcher with no token
gets `401`.

### What the turn command has to be, whatever CLI you use

Two properties, both of which cost an hour to discover the first time:

* **One turn, then exit.** Not an interactive session. Every CLI spells this differently.
* **Non-interactive.** A turn that stops to ask you for approval has put you back in the middle,
  which is the thing this whole arrangement removes. In Codex CLI v0.153.4 that is `exec` mode
  reporting `approval: never` at startup, and an `approval_mode` set for the `arc_*` tools does not
  interrupt there. Read what your CLI prints at startup and confirm it.

---

## 7. What goes in the agent's own file

The handshake already carries the channel's own rules: an MCP client receives them in the
`instructions` field of `initialize`, which is why a repository adopting ARC has nothing to copy
([P014](adr/P014-the-channel-explains-itself-in-the-handshake.md)). **Do not restate them.** Check
the mailbox at the start of a turn, ask versus notify, references rather than content, never both
wait at once, and what you know goes in the file and not only into the channel — all of that
arrives on its own.

What the handshake cannot know is anything about *your* project. That, and only that, is what
belongs in the agent's `AGENTS.md` or `CLAUDE.md`:

```markdown
# Agent `codex-research`

You work in parallel with `claude-lead`. This folder is a self-contained clone on
branch `<branch>`.

## You do not commit

Git does not work from inside your sandbox: it runs as a different user than the one
that owns this clone, so git refuses it as a dubious-ownership repository. Do not
fight it and do not work around it — write the files and stop there. `claude-lead`
reviews and integrates.

Do not create temporary files either. Deletion is blocked by policy, so anything you
write to clean up later, you cannot clean up.

## What "finished" means here

1. The file is written, with its sources cited by URL and date of consultation.
2. You answered with `arc_respond`, saying which files you wrote and the verdict.
3. The file answers **the question that was asked**, not a nearby one. Re-read the
   request before you call it done.

## What the user decides, not us

Product and method questions go to `claude-lead`, who takes them to the user. Do not
settle them on your own.
```

That is the whole template, and it is short on purpose. Every line above is either a fact about
this machine, this clone, or this project. The version this document arrived with was three times
longer, and the difference was the handshake's own rules copied back in — which is exactly the
per-repository copy P014 exists to prevent.

**One rule left this template and became the channel's.** "What you know goes in the file, not only
into the channel" was written here because it had been violated: the first pass answered richly in
the channel — figures, caveats, a verdict — and wrote a thinner report to disk, so the answer looked
complete and the gap was invisible until somebody opened the file. It generalises to anybody using
ARC, so it now ships in the handshake
([P025](adr/P025-what-earns-a-place-in-the-handshake.md)) and no project writes it down again.

"Answer the question that was asked" stayed, and the reason is the test in that same record: it is
about doing the work well rather than about using the channel.

---

## 8. The integrator's loop

The lead does not need anything to give *it* turns, because **a whole conversation fits inside one
of its own**. `arc ask --wait 300` blocks inside the current turn and returns the answer there, so
the lead can chain:

```
ask → block → read the answer → decide if it is finished →
    if not: ask again with the specific gaps → block → …
    if yes: review, commit, push, move to the next item
```

for as many rounds as the work takes, from a single "go" by the user. That is what makes the session
feel like talking to one agent rather than dispatching two.

To resume a wait without polling, let the channel do the waiting — **and branch on the exit code**:

```bash
# 3 is "no answer yet, ask again". Anything else is an error and must not be retried.
while arc await "$REQUEST_ID" --wait 300; rc=$?; [ $rc -eq 3 ]; do :; done
[ $rc -eq 0 ] || { echo "arc await failed with $rc" >&2; exit "$rc"; }
```

An `until arc await …; do :; done` looks equivalent and is not. `arc await` exits `1` immediately on
a hub that is down, a request id that does not exist, or one you did not send — so that loop turns
any of those into a tight spin against the hub, with the error discarded. The exit codes are
published contract ([P009](adr/P009-the-cli-exit-codes-are-contract.md)) precisely so this can be
branched on.

**The integrator's real job is refusing to close things.** "The agent replied" is not "the task is
done". Read the file, check it answers what was asked, and send it back with *specific* gaps when it
does not — naming the missing figure, the missing verdict, the unanswered half of the question. A
vague "please improve it" costs a turn and buys nothing.

---

## 9. Failures we actually hit

Ordered roughly by how much time each costs before you understand it. The **Host** column says
whether the cause is specific to the machine this was written on; everything unmarked is the
channel's own behaviour and is not.

| Symptom | Host | Cause | Fix |
|---|---|---|---|
| Agent reports the working folder "became inaccessible"; filesystem operations hang | any | It was given a **git worktree**; `.git` points outside its sandbox | Use a plain clone (§4) |
| `git` refuses to work: *detected dubious ownership* | Windows | The sandbox runs as a **different Windows user** than the owner of the clone, so git's `safe.directory` check rejects it. Symptoms downstream, including a `git commit` that appears to fail on `.git/index.lock`, are consequences of this | Let the integrator commit (§5) |
| A write-then-delete loop the agent never escapes | Windows | Deletion is `blocked by policy`; the write succeeds, the cleanup does not, and it retries | Tell the agent not to create temporary files (§5) |
| `Reading additional input from stdin...` then exit 1 | any | `codex exec` given the prompt as an argument with no console attached | Pass the prompt on stdin |
| Every agent gets `401`, config looks right | any | Stale demo token left in `~/.codex/config.toml` | Replace it; verify with a real `initialize` call (§3.3) |
| Token works nowhere and looks like `AAAAAAA…` | Windows | `RandomNumberGenerator::Fill` does not exist on PowerShell 5.1; it failed silently | `RNGCryptoServiceProvider`, plus a guard (§3.1) |
| `NativeCommandError`, wrong exit codes | Windows | Invoked `codex.ps1` instead of `codex.cmd`; the PowerShell wrapper wraps native stderr and pollutes `$LASTEXITCODE` | Call the `.cmd` |
| Accented characters corrupted in agent messages | Windows | PowerShell 5.1 reads a BOM-less `.ps1` as ANSI | Save as UTF-8 **with** BOM |
| A TOML section header that is not valid TOML | Windows | `printf` in bash interpreted `\f` in a Windows path | Heredoc, or a writer that does not interpret escapes |
| The agent commits the supervisor's log | any | Log file written inside the clone | Keep log and stop-file outside it (§6) |
| A watcher gets `401` on `/v1/observe/stream` | any | The observer exemption waives `X-ARC-Agent`, not the token | Send `X-ARC-Token` (§6) |
| A retry loop spins against the hub | any | `until arc await …` retries on exit `1` as readily as on `3` | Branch on the code (§8) |
| `422 bad_agent` | any | `ARC_AGENT` has a capital letter, a space, or is empty | `^[a-z0-9][a-z0-9._-]{0,63}$` |
| `422 invalid_wait` | any | `--wait` above the hub's `ARC_MAX_WAIT` | The hub refuses rather than silently clamping, and that is intentional |
| `422 self_addressed` | any | A request to yourself with a real wait, at either door | `--wait 0`; you cannot answer while blocked (§3.4) |
| `404` on `/v1/observe` | any | Not an endpoint | `/v1/observe/threads`, `/observe/history`, `/observe/stream` |
| An empty mailbox right after a successful read | any | The read **claimed** what it found | `arc inbox --replay 60` (§3.4) |

And the one that is not an error, only a wrong habit:

| **You are relaying messages by hand** | Everything sent with `--wait 0` and nothing opening the other agent's turns | §6 |

---

## 10. Cost, and stopping

Under the default shape there is nothing to stop: a turn is opened when a turn is needed, so an idle
session costs nothing.

Under the loop shape every supervisor turn is a real agent turn and costs tokens even when the
mailbox is empty, because the model still has to start in order to call `arc_inbox`. So **stop the
loop when the increment closes** — the session that wrote this ran two turns of work and would have
kept going indefinitely afterwards for nothing. Check the stop condition at the top of the loop
rather than signalling mid-turn, so stopping never interrupts work in progress.

Watch either shape at `/ui`. It shows what the history cannot: **who is blocked right now**, with
the clock running. A deadlock is recognisable at a glance — two open questions, each with its
counter climbing — and with turns opened automatically, nobody is watching a console when it
happens.

---

## 11. What still needs a human

Not much, and that is the point — but the remainder is not zero and should not be automated away:

- **Product and method decisions.** In the session this came from, the research surfaced that the
  official index they wanted to compare against covers one metropolitan area while theirs would be
  national. Neither agent should decide what to do about that.
- **Permission escalations.** Anything that widens what an agent may touch on the machine.
- **Starting and stopping the session**, and deciding what the increment is.

Everything between those is the two agents talking.

---

## 12. Appendix — the first attempt, and why it was wrong

Worth writing out because the failure was instructive.

The hub went up, both agents were configured, and the work was queued with `arc ask --wait 0` —
queue and move on, which is the correct mode for an agent that is not running. Then the user was
asked to open the second agent and tell it to check its inbox. It worked, and it was the wrong
shape: every subsequent message needed the same manual nudge. The channel was doing its job and the
human was still the transport.

`--wait 0` is right for a cold start and wrong as a way of working. Nothing in the setup was wrong;
what was missing was the standing instruction (§7) and something to open the other agent's turns
(§6).

**The sequence that works**, from nothing:

```powershell
# 1. Hub on loopback, token generated safely (§3.1)
docker build -t arc-hub .
docker run -d --name arc-hub --restart unless-stopped `
  -p 127.0.0.1:8765:8765 -v arc-data:/data -e ARC_TOKEN="$token" arc-hub
curl http://127.0.0.1:8765/healthz          # authenticated:true, max_wait_seconds:300

# 2. Identity for each agent, at user level (§3.2)
# 3. MCP registered for both, verified with a real initialize call (§3.3)
# 4. The cycle proved end to end against yourself (§3.4)
# 5. A shared remote, and one clone per agent — never a worktree (§4)
# 6. The project-specific file in that clone (§7), and nothing the handshake already says
# 7. The turn command configured for the second agent (§6)

# 8. One sentence to the lead. From here nobody touches anything.
```

**What it looked like when it worked.** The integrator reviewed two delivered reports, found that
neither answered the question that had been asked, and sent them back with the specific gaps.
Without any human involvement: the second agent got a turn, woke blocked in `arc_inbox`, worked for
225 seconds, rewrote both reports and answered. The integrator was blocked in `arc await` and had
the answer the instant it was sent. Then it reviewed again, fixed one wording error while
integrating, committed and pushed.

One round of real review, no messages relayed by hand.
