# Current work

**Nothing in progress.** Increments 01 to 07 are closed in [specs/](specs/).

**Last verified** (5 September 2026, at the close of increment 07):

* `dotnet build` — **0 warnings, 0 errors**, `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on
* `dotnet test` — **107 passed, 0 failed, 0 skipped**, 1 project
* `dotnet format --verify-no-changes` — clean
* `dotnet restore --force` — clean, no advisory
* `bash scripts/test-all.sh` — **four suites green, 76 checks** (29 REST, 18 CLI, 18 MCP, 11 UI)
* by hand against a running hub, reading the answers rather than their status codes: a third agent
  got the same 404 **byte for byte** on a real message and on an invented id, and on the real
  thread; the recipient got its body back with the accents intact; the CLI printed
  `El hub respondió 404 (not_found)` and exited **1**; and `/v1/observe`, with the token and no
  agent header at all, still listed that conversation and served its body — which is the
  deliberate behaviour this increment had to not break

Everything increment 06 verified about hosting — the container, the SDK image, Windows PowerShell
5.1, the installer's reachable half — was not re-run and is unaffected: this increment changed two
handlers and the rules, and touched nothing about how the hub is started. Its record stands in
[specs/06-a-machine-that-is-not-this-one.md](specs/06-a-machine-that-is-not-this-one.md).

---

## Opening the next one

This file becomes the plan: what the increment is for, and one row per phase with its commit.
**Every phase is closed in the same commit that updates its row**, and the increment is closed by
moving the narrative to [specs/](specs/) and leaving this file as it is now.

## What is closest to due

Nothing is committed to, and this is not a ranking — it is where [backlog.md](backlog.md) says a
trigger is nearest, so the next decision is taken against something rather than from a blank page.
Each entry there names what has to become true first; read those rather than this list.

* **Three findings about delivery**, all reachable by the usage the README describes: two polls by
  the same agent both receive the same messages, a message is marked delivered before the client
  has it, and a lost `note` is unrecoverable because `?unanswered=true` covers only requests. This
  is now the oldest group of findings still open, and the only one whose trigger is ordinary use
  rather than a decision.
* **`invalid_refs` promises a validation nothing performs**, and REST cannot emit it. `refs` is
  also unbounded while `body` is capped at 256 KB. Due when somebody decides what `refs` is — and
  it is the smallest of the open findings.
* **The service installation has still never been run end to end.** Everything past
  `install-hub.ps1`'s administrator check is unexecuted. Due the next time the hub is installed as
  a service, which is also the first time somebody follows the README to the end.
