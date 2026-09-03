# House ADRs

Decisions that can be dated as doctrine **before** this project: each one names what sustains it
outside this repository. They are the portable half of the collection — the project's own
decisions are in [../](../), numbered `P###`.

The package that owns them is published at <https://github.com/Guillervazio/dotnet-house>, and
this folder is a **copy** of its `adr/`. It stays a copy until a second project makes the transport
mechanism decidable — see [backlog.md](../../backlog.md). A change to one of these is a commit
**there** first, copied here after; editing it here alone forks the two.

Nothing about them is specific to this project; if one turns out to be, it is demoted to a `P###`
by the rule below.

The default for a new decision is `P`. A `P` is promoted here only when a **second** project takes
it independently, which is a file move. Removing an `H` that another project already obeys is an
investigation, and that asymmetry is why the default is `P`.
