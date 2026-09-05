# P015 — The licence is MIT

## Context

ARC stopped being a thing that runs on one desk the moment it was meant for other people to run.
Without a licence the default is that nobody may use it, whatever the README invites them to do,
so this had to be decided rather than left.

The alternatives were real. Apache-2.0 grants patent rights expressly and requires changes to be
stated; GPL-3.0 would oblige anybody distributing a modified hub to publish it.

## Decision

MIT. Copyright `guillervazio`, in [LICENSE](../../LICENSE).

## Consequences

Anybody may run the hub, change it and ship it inside something closed, keeping the notice. That
is the whole condition, and it is short enough that an adopter reads it — which matters more here
than any clause would: the thing being adopted is a channel two agents have to trust, and a
licence nobody finishes reading is one more reason to close the tab.

The patent grant Apache-2.0 would add protects against a risk this project does not have: there is
nothing patentable in long-polling a mailbox, and no company standing behind the repository for
anyone to sue. GPL's obligation would buy contributions back at the price of the adoption that is
the entire point of the increment this was decided in.

## What this does not authorise

Reading a permissive licence as a claim about anything else. It says who may use the code; it says
nothing about the channel being safe to expose, and the shared token still makes every holder able
to read every conversation — [P004](P004-one-token-and-an-agent-header.md),
[P006](P006-403-on-another-agents-mailbox.md), and the README section that spells both out.

Nor does it license adding a dependency under a licence that contradicts it. The rejected-package
list already turns one away for exactly that reason, and MIT does not make that check optional —
see [build-and-packages.project.md](../../.claude/rules/build-and-packages.project.md).
