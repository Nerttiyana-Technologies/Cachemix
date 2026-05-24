# Security Policy

## Supported versions

Cachemix has not yet reached a 1.0 release. Until then, security fixes are
applied to the latest published build only.

| Version       | Supported          |
| ------------- | ------------------ |
| latest (main) | :white_check_mark: |
| older builds  | :x:                |

Once 1.0 ships, this table will track the most recent minor release.

## Reporting a vulnerability

Please **do not** open a public issue for security problems.

Report vulnerabilities privately through GitHub's
[private vulnerability reporting](https://github.com/isureshsubramanian/Cachemix/security/advisories/new)
for this repository. If that is unavailable, email the maintainer at
**i.suresh.subramanian@gmail.com** with the subject line `Cachemix security`.

Please include the affected version or commit, a description of the issue, and
steps to reproduce or a proof of concept.

You can expect an acknowledgement within **5 business days** and a status update
within **15 business days**. If a vulnerability is accepted, a fix will be
prepared and released, and you will be credited in the advisory unless you ask
otherwise.

## Scope notes

Cachemix exposes a diagnostic dashboard at `/cachemix`. By design it is
**Development-only** unless explicitly opted in for other environments together
with an authorization filter. Reports that depend on deliberately disabling
these safeguards are out of scope. See the security model in
[`docs/architecture-and-roadmap.md`](docs/architecture-and-roadmap.md).
