# Increment 05 — the channel explains itself, and the hub starts by hand

Two things stood between ARC working and ARC being usable by somebody else, and neither was code
in the channel.

Adopting it meant pasting a block of rules into the consuming project's `CLAUDE.md` and
`AGENTS.md` — one copy per repository, and copies drift. And the README documented exactly one way
to run the hub: publish it, open an administrator console, install a Windows service. Nobody does
that the first time.

| # | Phase | Status | Commit |
|---|---|---|---|
| 1 | `ServerInstructions`: the MCP handshake carries how to use the channel | done | `586dfd8` |
| 2 | `scripts/start-hub.ps1`: run the hub by hand, on loopback or on the LAN | done | `764ac41` |
| 3 | `README.md` and `docs/AGENTS.md`: consuming ARC without touching the other repository | done | `764ac41` |

Phases 2 and 3 shared a commit because the README section phase 3 rewrites *is* the documentation
of the script phase 2 adds.

Verified at close (4 September 2026): `build` **0 warnings, 0 errors**; `test` **99 passed, 0
failed, 0 skipped**; `format --verify-no-changes` clean; `restore --force` clean; `bash
scripts/test-all.sh` **four suites green, 66 checks**; and `start-hub.ps1` driven by hand, which no
suite reaches.

---

## What the plan got wrong

| The plan said | What was true |
|---|---|
| `-Lan` "says what is missing rather than failing later", which assumed it could look | `Get-NetFirewallPortFilter` answers **access denied** to a normal user. Checking whether the rule exists needs the elevation the script exists to avoid. It names the rule instead of pretending to have looked — and the network profile, which *is* readable, it does read |
| `install-hub.ps1` was the working path, merely an inconvenient one | It has never been run in Windows PowerShell. Installing without `-Token` dies on `RandomNumberGenerator::GetBytes(int)`, a .NET Core static that .NET Framework does not have, and the IP it prints for the agents is the first non-loopback one — WSL's `172.22.160.1` here, not the LAN's `192.168.2.53` |
| Nothing about the scripts' own output | All three `.ps1` were UTF-8 with no BOM, which PS 5.1 reads as ANSI: every accented word they have ever printed came out as mojibake. Confirmed by running both encodings, not by reading about it |
| Phase 3 was about two documents | It was also about the quick try, which reached for `ARC_ALLOW_ANONYMOUS=1` — a testing switch standing in for the way to start the channel that did not exist yet |

The pattern in the first three rows is one thing: **writing a script is not running it.** Each was
found in the minute after typing `./scripts/start-hub.ps1`, and none of them was findable by
reading `install-hub.ps1`, which is where all three also live.

## What was decided

**[P014](../adr/P014-the-channel-explains-itself-in-the-handshake.md)** — the channel's rules
travel in `initialize`'s `instructions`, from one constant in the hub, rather than in every
repository that adopts the channel. Its alternatives were real: an MCP resource nothing obliges a
client to fetch, or an `arc_howto` tool the model must think of calling before it knows what the
channel is. What the hub guarantees is that the field arrives and is not empty; whether a client
puts it in front of its model is the client's decision, and `PROTOCOL.md` says so rather than
promising an injection this project does not control.

**The starting script never elevates.** Both topologies come from one `-Lan` switch, but the
firewall rule stays in `install-hub.ps1 -FirewallOnly`, because opening a port needs an
administrator and starting a hub does not. Making the common act administrative to save one rare
command would have been the wrong trade.

**A `.ps1` in this repository carries a BOM**, and `.editorconfig` pins `charset = utf-8-bom` for
them with the reason next to it. It is not a preference: without it the interpreter reads the file
as ANSI.

## `docs/AGENTS.md` was demoted, not deleted

It stops being "the text to paste into every repository" and becomes the fallback for where the
handshake does not reach — an agent driving `arc` from the command line, which has no handshake at
all, and a client that receives `instructions` and does not use them.

That leaves one second copy of the same advice, here, which is deliberate and is stated in the
file itself. What the increment was against is one copy *per consuming repository*; a single
fallback that covers the CLI is a different thing. It gained no second home either: the wiring for
a consuming repository stays in that file, and there is no `templates/`.

## Rules this made false

* `api-guidelines.project.md` was headed **"The MCP surface adds nothing REST lacks"**. It now
  does add something — a handshake REST has no equivalent for — so the heading says *no operation*
  and the section names the boundary between the three kinds of prose the MCP surface carries: a
  tool's `Description`, a tool's output, and the instructions.
* `protocol.project.md` enumerated what is breaking and what is not, and the handshake was in
  neither list. Dropping `instructions` or sending it empty is breaking; rewriting its text is
  not. Its *Three surfaces, one wire* section now names the asymmetry outright — the handshake is
  MCP's alone, so nothing an agent must obey may live only there.
* `architecture.project.md`'s *where a type goes* table had no row for prose addressed to a model.
  `ArcInstructions.cs` is one constant and no behaviour, and the table says what that does not
  license: not a tool's documentation, and not a rule of the channel.

## What is still not covered

`scripts/start-hub.ps1` has no automated test, and this repository has no mechanism for testing a
PowerShell script — adding one would be a new pattern for a 150-line script that ends in
`dotnet run`. It was verified by hand and the checks are written down in `todo.md`: started on
loopback and answered `/healthz` with `authenticated: true` and the database where it said; the
`-Lan` branch printed the firewall rule it does not create, named the network Windows classifies
as public, and chose the LAN address rather than WSL's.

The two defects it uncovered in `install-hub.ps1` are in `docs/backlog.md` with their trigger.
They are not fixed here because fixing the installer changes what the installer does, and the
commit that added a different script is not the place to find that out.
