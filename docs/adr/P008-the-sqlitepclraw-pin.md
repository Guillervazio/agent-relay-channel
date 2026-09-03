# P008 — The SQLitePCLRaw pin, and what re-checks it

## Context

`Microsoft.Data.Sqlite` 10.0.1 depends transitively on `SQLitePCLRaw.bundle_e_sqlite3` at a
version carrying GHSA-2m69-gcr7-jv3q.

## Decision

`Directory.Packages.props` pins it to 2.1.13, above the affected range, with
`CentralPackageTransitivePinningEnabled` so the pin reaches a dependency nothing references
directly.

## Consequences

The advisory is cleared without waiting on an upstream release.

The pin is also the reason the obvious check is worthless. `dotnet list package --vulnerable
--include-transitive` comes back clean **because of** the pin, so it can never report that the pin
has become unnecessary — it will say the same thing on the day the dependency is fixed and on the
day it is not.

**The instrument is `Microsoft.Data.Sqlite`'s own nuspec.** Read what the package declares. When
it declares a fixed version, the pin goes in the same commit.

## What this does not authorise

Adding a pin without naming its instrument. The general form of the mistake above is a check that
is satisfied by the thing it is meant to be checking, and it is not specific to vulnerability
reports.
