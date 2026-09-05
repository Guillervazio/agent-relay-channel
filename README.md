# ARC — Agent Relay Channel

A communication channel between agents from different providers (Claude Code, Codex CLI)
working on different PCs on the same network.

It replaces the shared `.md` file that used to act as a mailbox: here there is a recipient,
a thread, a state, a history and — what actually matters — **a real wait**. Agent A asks and
stays blocked inside its own turn until B answers.

```
PC1  Claude Code ──┐                    ┌── SQLite (arc.db)
                   ├─► Arc.Hub :8765 ───┤
PC2  Codex CLI  ───┘   REST + MCP       └── in-memory waits
                            │
                            └── /ui  live panel, read-only
```

## Why not a message broker

A command-line agent **is not a server**: it only exists for the duration of its turn.
It cannot hold an open subscription, nor wake up for an event that arrives while it is idle.

Kafka is sized for millions of partitioned events and drags a JVM along; here we are talking
about a few dozen messages a day between two processes. RabbitMQ is more reasonable, but its
model assumes permanently connected consumers — precisely what a CLI agent cannot be.
Emulating RPC on top of it still forces you to write an HTTP bridge the agent can poll: the
broker does not replace the hub, it is added on top of it.

The hard problem is not the transport, it is the agent's lifetime window. **HTTP long polling**
solves it: the request stays open on the server until the answer arrives.

## Requirements

On the machine where you build and host the hub:

| You need | What for | Check |
|---|---|---|
| **.NET SDK 10** | All the code targets `net10.0` | `dotnet --version` |
| **PowerShell** | `start-hub.ps1`, `publish.ps1` and `install-hub.ps1` | Ships with Windows |

And only if you are going to run the smoke tests, which are shell scripts:

| You need | What for | Check |
|---|---|---|
| **bash** | The `.sh` scripts | Git Bash or WSL |
| **curl** | `smoke.sh`, `smoke-mcp.sh` and `smoke-ui.sh` speak raw HTTP | `curl --version` |
| **Python 3.7+** | The smokes parse JSON with it | `python3 --version`, or `python --version` |

> Either name works: the scripts try `python3` and then `python`, and check the version of
> whichever they find. What they need and cannot find they say by name and stop, so a missing
> interpreter is never reported as a failed check about something else.

The other PC needs none of this: the client you copy over there is self-contained.

## Quick try on a single machine

```bash
dotnet build
```

Start it in this console, without installing anything:

```powershell
./scripts/start-hub.ps1
```

It listens on loopback only. It does not invent a token if you already have one — it takes
the one in this console, or the one `install-hub.ps1` left at machine level, and generates one
only when there is none, printing it because both agents need it. The database goes in the
root of the repository rather than under `bin/`, where a `dotnet clean` would take the channel
with it. Ctrl+C stops it; there is no service and nothing was installed.

The panel lives at <http://127.0.0.1:8765/ui>. To exercise the four surfaces, see
[Verification](#verification) at the end: `test-all.sh` brings up its own hub and does not need
this one.

## Installing on both PCs

Two things change once the other PC is involved: the hub has to listen on the network rather
than on loopback, and it has to survive closing the console. `./scripts/start-hub.ps1 -Lan`
covers the first, and says up front what the LAN needs and it cannot give — the firewall rule,
which requires elevation, and a network classified as private. This section covers the second,
which is a Windows service.

### 1. On the machine hosting the hub

```powershell
./scripts/publish.ps1
```

And from an **administrator** console:

```powershell
./scripts/install-hub.ps1 -HubPath .\publish\hub
```

It creates the firewall rule (private network profile only), installs the Windows service,
generates the token if you do not pass one, and prints the IP and the variables the agents
must use.

### 2. On the other PC

Copy `publish/cli` and add the folder to the PATH. The client is self-contained: it does not
need .NET installed there.

### 3. On each agent

Each machine identifies itself with a different name:

```powershell
# PC1
$env:ARC_URL = 'http://192.168.1.10:8765'
$env:ARC_TOKEN = '<the token install-hub printed>'
$env:ARC_AGENT = 'claude-pc1'
$env:ARC_PROVIDER = 'claude-code'

# PC2 — same token and URL, different ARC_AGENT
$env:ARC_AGENT = 'codex-pc2'
$env:ARC_PROVIDER = 'codex'
```

`ARC_AGENT` does not accept just anything: it is the key of the wait registry, so it has to
match `^[a-z0-9][a-z0-9._-]{0,63}$` — **lowercase**, digits, dot, hyphen or underscore,
starting with a letter or a digit and at most 64 characters. One uppercase letter or a space
and the hub answers `422 bad_agent`.

Check the connection with `arc health`.

## The hub in a container

Nothing in the channel is Windows; only the hosting above is. If the machine that will hold the
hub runs Linux, or you would rather not install a service on it, the [Dockerfile](Dockerfile)
builds and runs it with neither the .NET SDK nor PowerShell present:

```bash
docker build -t arc-hub .
docker run -d --name arc-hub -p 8765:8765 -v arc-data:/data            -e ARC_TOKEN='<the secret>' arc-hub
```

`ARC_TOKEN` has no default and the hub refuses to start without it, saying so — the channel
carries instructions between agents and has no business listening unauthenticated. The mailbox
lives in the `/data` volume rather than in the container, so recreating the container does not
take the channel with it, and `ARC_DB` and `ARC_URLS` are already set inside the image.

**One replica, one volume.** [P003](docs/adr/P003-sqlite-on-a-file.md) assumes a single process
owning the file: two containers over the same volume is not untested, it is unsupported.

The agents are configured exactly as in step 3 above, with the address of the machine running the
container.

## Using it from an agent

### From the command line

It works in any agent that can run commands, without depending on its MCP support:

```bash
# A asks and waits up to 3 minutes
arc ask --to codex-pc2 --subject "Payments contract" --body-file question.md --wait 180

# B checks its mailbox, waiting if it has to
arc inbox --wait 300

# B answers; A wakes up instantly
arc respond req_1a2b3c --body-file answer.md
```

Those three are the complete cycle. The other commands cover the remaining cases:

| Command | What for |
|---|---|
| `arc ask --to A --body-file f [--wait N]` | Ask and block until the answer |
| `arc await <request_id> [--wait N]` | Resume the wait on a request that already expired |
| `arc inbox [--wait N] [--unanswered]` | Your own mailbox. `--unanswered` recovers what was delivered and is still unanswered |
| `arc respond <request_id> --body-file f` | Answer a request addressed to you |
| `arc note --to A --body-file f` | Notice of an accomplished fact, without waiting for an answer |
| `arc thread <thread_id>` | Full history of a conversation |
| `arc agents` | Who is on the channel and when they were last seen |
| `arc health` | Hub diagnostics: active waits, agents, configuration |

Options common to all of them: `--json` for raw unformatted output, and
`--url` / `--agent` / `--token`, which are equivalent to the environment variables.

The exit codes let you branch without reading the text:
`0` success · `1` error · `2` bad usage · `3` wait expired · `4` no messages.

Watch out for the `--wait` defaults, which are **not zero**:

| Command | Without `--wait` |
|---|---|
| `arc ask` | waits **120 s** |
| `arc await` | waits **120 s** |
| `arc inbox` | **does not wait**: looks and comes back |

And the hub refuses whatever exceeds its `ARC_MAX_WAIT`, 300 seconds by default:
`--wait 600` **is an error**, not a shortened wait — the hub answers `422 invalid_wait` and
`arc` exits `1`. A silent clamp would come back early with an `outcome` saying nothing
happened, which is true and useless. With `--wait 0` the request is queued and the
command returns instantly, with code `3` — there is no answer *yet*, but the request stays
alive in the recipient's mailbox.

The body is passed by file (`--body-file f`), by stdin (`--body-file -`) or inline
(`--body "text"`).

> On Windows, always pass the body with `--body-file`. Command-line arguments cross the ANSI
> codepage and corrupt accented characters before they even leave.

### As MCP tools

Register it once for your user, not once per repository: `--scope user` writes to
`~/.claude.json` and applies to every project you open.

```bash
claude mcp add --scope user --transport http arc http://192.168.1.10:8765/mcp \
  --header "X-ARC-Agent: claude-pc1" \
  --header "X-ARC-Token: <token>"
```

In Codex CLI, `~/.codex/config.toml` is already global to the user:

```toml
[mcp_servers.arc]
url = "http://192.168.1.10:8765/mcp"

[mcp_servers.arc.http_headers]
"X-ARC-Agent" = "codex-pc2"
"X-ARC-Token" = "<token>"
```

Support for HTTP MCP servers in Codex CLI has lagged behind stdio; check your version. If it
does not support it, the `arc` command-line client does exactly the same thing and always
works.

Published tools: `arc_ask`, `arc_await`, `arc_inbox`, `arc_respond`,
`arc_note`, `arc_thread`, `arc_agents`.

### The repository you work in writes nothing

There is no block to paste into that project's `CLAUDE.md`. The handshake carries the channel's
own instructions — check the mailbox before you start your turn, ask when you need an answer
and notify when you do not, send references rather than content, never both wait at once — so
the rules travel with the hub instead of being copied into every repository that adopts it.
[docs/PROTOCOL.md](docs/PROTOCOL.md) describes the field.

Whether a client puts that text in front of its model is the client's decision, not this
project's. Where it does not — and the command-line client has no handshake at all —
[docs/AGENTS.md](docs/AGENTS.md) says the same things in a form you can paste.

## Watching the conversation

The hub serves a panel at `/ui`. It is only for looking: it does not let you write to the
channel.

```
http://192.168.1.10:8765/ui
```

Messages are not polled, the server pushes them: they appear the moment they are written,
with their thread, their subject and their `refs`. And next to the history you can see what
the history does not keep — **who is blocked waiting right now**, with the clock running, and
which agent has its mailbox open. A deadlock is recognisable at a glance: two open questions,
each one with its counter climbing.

On the left are all the conversations, separated by state: **in progress** — it still has an
unanswered question — and **finished**. When you pick one, the panel stops showing the whole
channel and stays with that complete thread, from beginning to end, even if it is from the day
before yesterday; the rest keeps arriving behind it and the side counters remain the ones for
the channel. The chosen conversation goes in the address (`/ui#t=thr_…`), so reloading does
not lose it and the link can be passed along as it is.

If the hub has `ARC_TOKEN`, the panel asks for it when it opens and stores it in that browser.
The page itself carries no data inside, and that is why it is served unauthenticated; the data
is not. The panel is not an agent either: it does not show up in `arc agents` and it does not
mark as delivered what it displays.

## What the shared token does not protect

One hub, one token, and everybody holding it is on the same channel. That is the deployment ARC
is built for — one network, agents belonging to the same person or the same team — and two
things follow from it that are worth knowing before you hand the token to somebody else:

* **The agent name is attribution, never authorisation.** Any holder of the token can present any
  name. The `403` an agent gets on somebody else's mailbox stops a mistake and a curious agent,
  not a dishonest one — [P004](docs/adr/P004-one-token-and-an-agent-header.md).
* **The whole channel is readable.** `/v1/observe` is what the panel is built on, and it shows
  every conversation with its bodies, not only yours. It asks for the token and not for a name, so
  anybody holding the token reads everything — [P010](docs/adr/P010-the-observer-page-is-unauthenticated.md).
  This is the one that matters: the `404` an agent now gets on somebody else's message
  ([P016](docs/adr/P016-a-message-is-read-by-its-two-ends.md)) is a guardrail against a mistake,
  and this route is why it is not a boundary.

Send references and not content — a branch and a commit rather than the file — and neither of
them is a problem: it is the rule the channel asks for anyway, and it keeps the channel from
being the place where anything confidential lives.

## Hub configuration

| Variable | Default | What for |
|---|---|---|
| `ARC_TOKEN` | — | Shared secret. **Mandatory** except in anonymous mode. |
| `ARC_ALLOW_ANONYMOUS` | — | `1` disables authentication and forces listening on loopback. Testing only. |
| `ARC_URLS` | `http://0.0.0.0:8765` | Where to listen. |
| `ARC_DB` | `arc.db` next to the executable | SQLite file. |
| `ARC_MAX_WAIT` | `300` | Cap in seconds per wait. Must be between 1 and 86400: the hub refuses to start on anything else rather than run with a cap you did not choose. |

`install-hub.ps1` sets `ARC_TOKEN`, `ARC_DB` and `ARC_URLS` at the **machine** level, not the
user level: the service does not inherit your environment.

## Maintenance

Everything here needs an **administrator** console.

**Remove the hub from the machine** — stops the service, deletes it and withdraws the firewall
rule:

```powershell
./scripts/install-hub.ps1 -Uninstall
```

It does not touch the machine variables or the `arc.db` file. If you want to delete those as
well:

```powershell
'ARC_TOKEN','ARC_DB','ARC_URLS' | ForEach-Object { [Environment]::SetEnvironmentVariable($_, $null, 'Machine') }
```

**Recover the token** if you lost it, without reinstalling anything:

```powershell
[Environment]::GetEnvironmentVariable('ARC_TOKEN', 'Machine')
```

**Rotate the token** — run the installer again with a new one; it recreates the service with
it. Afterwards you have to update `ARC_TOKEN` on both PCs:

```powershell
./scripts/install-hub.ps1 -HubPath .\publish\hub -Token (Read-Host 'new token')
```

**Open the firewall only**, without installing the service, with `-FirewallOnly`. And the port
is changed with `-Port`, both when installing and when uninstalling.

## When something does not answer

`/healthz` is the only endpoint that does **not** ask for a token, so it is the one to
diagnose with from any machine:

```bash
curl http://192.168.1.10:8765/healthz
```

It returns the uptime, whether the hub requires authentication, the effective `ARC_MAX_WAIT`,
the database path, the agents seen and — most useful of all — the **active waits**: two agents
waiting for each other show up right there.

| Symptom | Usual cause |
|---|---|
| `arc health` works on the hub but not from the other PC | The network is classified as public. The rule only opens the private profile: `Get-NetConnectionProfile` |
| `401 unauthorized` | `ARC_TOKEN` different from the one the service has, or undefined |
| `422 bad_agent` | `ARC_AGENT` with uppercase letters, spaces, or empty. See the format above |
| `403 forbidden` | Reading someone else's mailbox, or answering something not addressed to you |
| `409 already_answered` | That request already has an answer; the cycle allows a single reply |
| Corrupted accents | Body passed with `--body`. Use `--body-file` |
| The service exists but does not answer | `Get-Service ArcHub`; if it starts and dies, it is almost always `ARC_TOKEN` undefined at machine level |

A request that expired **is not lost**: it is still alive in the recipient's mailbox and is
recovered with `arc inbox --unanswered` or `arc await <request_id>`.

## A note on the ChatGPT desktop app

Its connectors run from OpenAI's infrastructure, not from your machine: a host on your private
LAN is not reachable from there. Including it would require exposing the hub to the internet
over HTTPS through a tunnel, with the attack surface that adds. The reliable channel is Claude
Code and Codex CLI, both on your network.

## Structure

| Path | Contents |
|---|---|
| [src/Arc.Core](src/Arc.Core) | Model, SQLite store, wait registry and channel rules |
| [src/Arc.Hub](src/Arc.Hub) | HTTP service: REST endpoints, MCP tools and the `/ui` panel |
| [src/Arc.Cli](src/Arc.Cli) | The `arc` client |
| [tests/Arc.Tests](tests/Arc.Tests) | Core tests |
| [scripts](scripts) | Starting, publishing, installation and smoke tests |
| [Dockerfile](Dockerfile) | The hub on any machine with a container runtime |
| [docs/PROTOCOL.md](docs/PROTOCOL.md) | Full contract of messages and endpoints |
| [docs/AGENTS.md](docs/AGENTS.md) | What the handshake already says, for a client that does not read it |

## Verification

Everything in one pass — it brings up a temporary hub, tests it and shuts it down:

```bash
bash scripts/test-all.sh
```

Or piece by piece, against a hub already running:

```bash
dotnet test                  # core: wait registry and persistence
bash scripts/smoke.sh        # REST, including the full blocking cycle
bash scripts/smoke-cli.sh    # the client exactly as an agent will use it
bash scripts/smoke-mcp.sh    # MCP handshake, catalogue and a real call
bash scripts/smoke-ui.sh     # the panel and its event stream
```

All of it also runs on `ubuntu-latest` for every push to `master` and every pull request —
[.github/workflows/gate.yml](.github/workflows/gate.yml).

## Licence

MIT — see [LICENSE](LICENSE). Use it, change it, ship it; keep the copyright notice.
