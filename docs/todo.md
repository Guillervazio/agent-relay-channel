# Current work

Increment 04 — a seam for the three surfaces. Increments 01 to 03 are closed in
[specs/](specs/).

**Last verified** (4 September 2026, at the close of increment 03):

* `dotnet build` — **0 warnings, 0 errors**, `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` on
* `dotnet test` — **52 passed, 0 failed, 0 skipped**, 1 project
* `dotnet format --verify-no-changes` — clean
* `bash scripts/test-all.sh` — **four suites green, 64 checks** (24 REST, 15 CLI, 14 MCP, 11 UI)

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | `HubOptions` and `HubApp.Build`: the hub takes its configuration instead of reading the environment | not started |  |
| 2 | Tests for the hub through `Microsoft.AspNetCore.TestHost` — needs the package approved | not started |  |
| 3 | `CliRunner` with its I/O injected, and the exit codes asserted without a hub | not started |  |
| 4 | The rules stop saying these surfaces cannot be tested | not started |  |

Every phase is closed in the same commit that updates its row.

---

## Why this is a design question before it is a testing one

`Arc.Hub/Program.cs`, `Arc.Hub/ArcTools.cs` and `Arc.Cli/Program.cs` are 1,144 lines with **zero**
xunit coverage. The 401, the `bad_agent` 422, the 200-versus-202 mapping, the empty mailbox's 204
and all seven MCP tools are exercised only by `scripts/test-all.sh` — which no gate runs, and which
`CLAUDE.md` warns arrives as a plugin whose every failure mode is silent.

It cannot be fixed by writing tests, because there is nothing to write them against: both files are
top-level statements that read `Environment.GetEnvironmentVariable` as their first act. Two
increments have now had to say "no unit test, verified by hand" — increment 03 for `ARC_MAX_WAIT`
and for the CLI's `--refs` — and that is the debt this pays.

The shape is the one `ChannelService` already has: configuration in through the constructor, not
read from ambient state. `Program.cs` keeps exactly what cannot be tested — reading the environment,
printing the refusal, `app.Run()` — and hands everything else a `HubOptions`. That also removes the
reason two hub tests could never run at once.

`EventStream` has no test either, and `ChannelServiceTests` builds the service **without** one, so
its publish branches never execute. `PROTOCOL.md`'s `delivered` event, its two-second `: ping` and
its `DropOldest` promise are asserted by nothing anywhere. That belongs to phase 2.
