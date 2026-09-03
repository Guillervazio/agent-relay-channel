# Decision records

One file per decision that had an alternative worth naming. Each carries its context, the
decision, its consequences, and — the part that keeps an exception from spreading — **what it does
not authorise**.

Two collections with independent numbering:

* `P###`, here: decisions of this project.
* `H###`, in [house/](house/): doctrine that can be dated **before** this project, each naming
  what sustains it outside this repository.

The default for a new decision is `P`. Promotion to `H` needs a second project to have taken the
same decision independently — see [house/README.md](house/README.md) for why the asymmetry is
deliberate.

A rule links to the record it needs; the record is not repeated in the rule. The narrative of the
increment a decision was taken in lives in [../specs/](../specs/), which is the other half: a spec
says what happened, an ADR says what was decided and what it forbids.
