# Current work

## Increment 06 — a machine that is not this one

ARC stops being a thing that runs here and becomes a thing somebody else can run. Nothing in the
channel changes: [P003](adr/P003-sqlite-on-a-file.md), [P004](adr/P004-one-token-and-an-agent-header.md)
and [P010](adr/P010-the-observer-page-is-unauthenticated.md) all assume one hub per network with
one shared token, and that is exactly the deployment a tool anybody installs has. What changes is
everything around it: the one documented way to host the hub is a Windows service, the installer
that documents it has never been run in the console it targets, the smokes call an interpreter
that only exists on this machine, and there is no licence saying anybody may use any of it.

This is the increment where four backlog entries whose trigger was written in advance become due
at once, because the trigger they all named is the same: a second machine.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | `install-hub.ps1`: the two failures found while writing `start-hub.ps1` | done | this commit |
| 2 | The smokes say which interpreter they could not find, and find the one that exists | pending | |
| 3 | `LICENSE`, and a README that says what the shared token does not protect | pending | |
| 4 | `Dockerfile`: hosting the hub without Windows | pending | |
| 5 | `.github/workflows/`: the gate on a machine that is not this one | pending | |

Phases 1 and 2 come first because they are defects that hit the first adopter, not features.
Phase 5 comes last because CI has nothing to prove until there is something to publish.

### What each phase is for

**1.** [backlog.md](backlog.md) records both failures: installing without `-Token` dies on
`RandomNumberGenerator::GetBytes(int)`, a .NET Core static that Windows PowerShell's .NET Framework
does not have, and the IP printed for the agents is the first non-loopback one — WSL's here, not
the LAN's. Both corrections already exist next door in `start-hub.ps1`; this phase moves them.

**2.** `jget()` calls `python` and swallows stderr, so a missing interpreter surfaces as a content
mismatch about something else. The backlog names the fix in advance as a `require python`
preflight. That is not enough: on the runner of phase 5 the interpreter is `python3`, and on this
machine `python3` does not exist. The preflight has to pick.

**3.** MIT. Without a licence, "anybody can use it" is false, and the README has to say what
[P004](adr/P004-one-token-and-an-agent-header.md) means for somebody who did not write it: every
holder of the token can read the whole channel.

**4.** The hub is `net10.0` and portable; only its hosting is Windows. A container with a volume
for `arc.db` is one replica, which is what [P003](adr/P003-sqlite-on-a-file.md) requires.

**5.** Build, test, format and the four smoke suites on `ubuntu-latest`. The suites have never run
anywhere but here, which is the whole point.

---

## Opening the next one

This file becomes the plan: what the increment is for, and one row per phase with its commit.
**Every phase is closed in the same commit that updates its row**, and the increment is closed by
moving the narrative to [specs/](specs/) and leaving this file as it was.

## Last verified

4 September 2026, at the close of phase 1:

* `dotnet build` — **0 warnings, 0 errors**, `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on
* `dotnet test` — **99 passed, 0 failed, 0 skipped**, 1 project
* `dotnet format --verify-no-changes` — clean
* `dotnet restore --force` — clean, no advisory
* `bash scripts/test-all.sh` — **four suites green, 66 checks** (24 REST, 15 CLI, 16 MCP, 11 UI)
* `scripts/ArcHost.ps1` in **Windows PowerShell 5.1**, the edition both defects were about:
  `New-ArcToken` returned a token, and `Get-ArcLanAddress` returned `192.168.2.53` — the LAN's,
  not WSL's `172.22.160.1`
* `install-hub.ps1 -FirewallOnly` unelevated: it reaches its administrator check, so the
  dot-source resolves before anything else in the script runs
* `start-hub.ps1` by hand on both branches, which no suite reaches: loopback answered `/healthz`
  with `authenticated: true`, and `-Lan` announced `http://192.168.2.53:8799`
