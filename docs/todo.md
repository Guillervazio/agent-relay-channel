# Current work

**Nothing in progress.** Increments 01 to 06 are closed in [specs/](specs/).

**Last verified** (5 September 2026, at the close of increment 06):

* `dotnet build` — **0 warnings, 0 errors**, `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on
* `dotnet test` — **99 passed, 0 failed, 0 skipped**, 1 project
* `dotnet format --verify-no-changes` — clean
* `dotnet restore --force` — clean, no advisory
* `bash scripts/test-all.sh` — **four suites green, 66 checks** (24 REST, 15 CLI, 16 MCP, 11 UI)
* the same suite **inside `mcr.microsoft.com/dotnet/sdk:10.0`** against a copy of the working
  tree — four suites, 66 checks, green: the first time any of it has run outside Windows
* the container by hand, which no suite reaches: it refused to start with no `ARC_TOKEN` and said
  so, ran as `app` rather than root, kept its threads across `docker rm` and a rebuild, started
  with no warnings, and answered all four suites through the published port
* `scripts/ArcHost.ps1` in **Windows PowerShell 5.1**, the edition both installer defects were
  about: `New-ArcToken` returned a token and `Get-ArcLanAddress` returned `192.168.2.53`, the
  LAN's, not WSL's `172.22.160.1`
* `install-hub.ps1 -FirewallOnly` unelevated, which reaches its administrator check — the service
  installation past that point is still unrun, and is in [backlog.md](backlog.md)
* the preflight's failure paths: with `python` off the `PATH` `smoke.sh` named the interpreter and
  exited **1**, and with no `curl` so did `smoke-mcp.sh`

---

## Opening the next one

This file becomes the plan: what the increment is for, and one row per phase with its commit.
**Every phase is closed in the same commit that updates its row**, and the increment is closed by
moving the narrative to [specs/](specs/) and leaving this file as it is now.

## What is closest to due

Nothing is committed to, and this is not a ranking — it is where [backlog.md](backlog.md) says a
trigger is nearest, so the next decision is taken against something rather than from a blank page.
Each entry there names what has to become true first; read those rather than this list.

* **The workflow has never run on GitHub's runners.** It resolves itself on the first push, and
  the first push is the test.
* **The channel has no confidentiality.** `GET /v1/messages/{id}` and `GET /v1/threads/{id}`
  perform no authorisation at all, which defeats the 403 `InboxAsync` raises and is why
  [P006](adr/P006-403-on-another-agents-mailbox.md) has to say the protection H011 assumes does
  not exist here. The README now states this where the token is handed over, which makes it a
  documented property rather than a surprise — and moves nothing about when it is due: before the
  channel carries anything one agent must not read.
* **Three findings about delivery**, all reachable by the usage the README describes: two polls by
  the same agent both receive the same messages, a message is marked delivered before the client
  has it, and a lost `note` is unrecoverable because `?unanswered=true` covers only requests.
