# Current work

**Nothing in progress.** Increments 01 to 05 are closed in [specs/](specs/).

**Last verified** (4 September 2026, at the close of increment 05):

* `dotnet build` — **0 warnings, 0 errors**, `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on
* `dotnet test` — **99 passed, 0 failed, 0 skipped**, 1 project
* `dotnet format --verify-no-changes` — clean
* `dotnet restore --force` — clean, no advisory
* `bash scripts/test-all.sh` — **four suites green, 66 checks** (24 REST, 15 CLI, 16 MCP, 11 UI)
* `scripts/start-hub.ps1` by hand, which no suite reaches: started on loopback and answered
  `/healthz` with `authenticated: true` and the database where it said; the `-Lan` branch printed
  the firewall rule it does not create, named the network Windows classifies as public, and chose
  the LAN address rather than WSL's

---

## Opening the next one

This file becomes the plan: what the increment is for, and one row per phase with its commit.
**Every phase is closed in the same commit that updates its row**, and the increment is closed by
moving the narrative to [specs/](specs/) and leaving this file as it is now.

## What is closest to due

Nothing is committed to, and this is not a ranking — it is the three places
[backlog.md](backlog.md) says a trigger is nearest, so the next decision is taken against
something rather than from a blank page. Each entry there names what has to become true first;
read those rather than this list.

* **The channel has no confidentiality.** `GET /v1/messages/{id}` and `GET /v1/threads/{id}`
  perform no authorisation at all, which defeats the 403 `InboxAsync` raises and is why
  [P006](adr/P006-403-on-another-agents-mailbox.md) has to say the protection H011 assumes does
  not exist here. Due before the channel carries anything one agent must not read.
* **Three findings about delivery**, all reachable by the usage the README describes: two polls by
  the same agent both receive the same messages, a message is marked delivered before the client
  has it, and a lost `note` is unrecoverable because `?unanswered=true` covers only requests.
* **`install-hub.ps1` fails twice in Windows PowerShell**, found while writing `start-hub.ps1`:
  the token it generates without `-Token` dies on a .NET Core static, and the IP it prints is the
  first non-loopback one rather than the one that answers from outside. The corrections are
  already written, next door in `start-hub.ps1`.
