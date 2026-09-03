---
paths:
  - "**/*.csproj"
  - "**/*.props"
  - "**/*.slnx"
  - "global.json"
---

# Build and packages — this project

Appendix to [shared/build-and-packages.md](shared/build-and-packages.md).

## The projects, and what owns what

Four projects: `src/Arc.Core`, `src/Arc.Hub`, `src/Arc.Cli`, `tests/Arc.Tests`.

`Arc.Core` is the only project that references the store's packages. Two base clauses have no
subject here and are recorded as **not applicable** rather than as deviations, because a clause
with nothing to bind to is not being departed from:

* *"The domain project references no NuGet package."* There is no domain project. `Arc.Core` is
  the whole model **and** the store, which is a decision recorded in
  [P003](../../docs/adr/P003-sqlite-on-a-file.md), not an oversight.
* *"One project owns the ORM's packages."* There is no ORM. `Microsoft.Data.Sqlite` is a driver
  and `Arc.Core` is the one project that has it.

## Approved

| Package | Project | What it is for |
|---|---|---|
| `Microsoft.Data.Sqlite` | Arc.Core | The store. ADO.NET over SQLite, one connection per operation |
| `SQLitePCLRaw.bundle_e_sqlite3` | Arc.Core | The native SQLite bundle `Microsoft.Data.Sqlite` needs. Present explicitly only because of the pin below |
| `ModelContextProtocol.AspNetCore` | Arc.Hub | The MCP surface: seven tools over the same `ChannelService` the REST surface calls |
| `Microsoft.NET.Test.Sdk` | Arc.Tests | Test host |
| `xunit` | Arc.Tests | The test framework |
| `xunit.runner.visualstudio` | Arc.Tests | VSTest discovery for xunit v2 |
| `coverlet.collector` | Arc.Tests | Coverage collection. **Nothing reads its output today** — see `docs/backlog.md` |

Seven rows against seven `PackageVersion` entries in `Directory.Packages.props`. Complete.

`Arc.Cli` has no packages at all: it is `System.Net.Http` and a project reference.

## Rejected

| Package | Reason it was refused |
|---|---|
| `FluentAssertions` | Version 8 changed to a licence that is not free for commercial use. The base names `Assert` as the fallback and the 21 existing tests already use it |
| `Swashbuckle` / `NSwag` | ARC publishes `docs/PROTOCOL.md` as its contract and has no generated OpenAPI document. A second, generated description of the same wire is a second thing to keep true |

## Pinned

| Package | Version | Why | What re-checks whether the pin is still needed |
|---|---|---|---|
| `SQLitePCLRaw.bundle_e_sqlite3` | 2.1.13 | `Microsoft.Data.Sqlite` 10.0.1 pulls it transitively at a version carrying GHSA-2m69-gcr7-jv3q | **`Microsoft.Data.Sqlite`'s own nuspec.** Read what it declares its dependency to be. `dotnet list package --vulnerable --include-transitive` is clean *because of* this pin, so a clean report is not evidence the pin can go |

## Central package management

`Directory.Packages.props` holds every version; no `.csproj` carries one, and
`CentralPackageTransitivePinningEnabled` is what makes the pin above reach a transitive
dependency. `Directory.Build.props` holds the settings shared by all four projects.

`global.json` pins the SDK feature band to the one the style rules were measured against, with
`rollForward: latestFeature` so a patch upgrade is allowed and a band change is a decision.

## Migrations

The base's *Migrations* section has no subject: there is no ORM and no migration tool. ARC's
schema is created by `MessageStore.InitializeAsync`, called at hub startup — which contradicts
[H009](../../docs/adr/house/H009-migrations-never-run-at-application-startup.md) knowingly, for
the reasons in [P007](../../docs/adr/P007-the-schema-is-created-at-startup.md).

## Deviations

None. The two clauses above with no subject are recorded as not applicable, which is a different
thing and does not spend a deviation.
