# Security policy

## Supported versions

Orvano is **pre alpha** and has no releases yet. Only the `main` branch is looked at, and nothing in it is ready for real data. A full security review is planned before v1.0.

## Reporting a vulnerability

Please report vulnerabilities **privately**, through GitHub's private vulnerability reporting:

1. Go to the [Security tab](https://github.com/orvanohq/orvano/security) of this repository.
2. Choose **Report a vulnerability** (or open [a new advisory](https://github.com/orvanohq/orvano/security/advisories/new) directly).

Please don't open a public issue, pull request or discussion about it.

A helpful report includes:

- what is affected (a module, endpoint, the CLI, the Compose setup) and the commit you tested
- steps to reproduce, or a small proof of concept
- what an attacker could do with it, as you see it

## What happens next

We will acknowledge your report, keep you updated while we investigate, and agree a disclosure date with you once a fix is ready. We will credit you in the advisory unless you would rather stay anonymous.

## Out of scope

- Deployments you don't own or have permission to test
- Denial of service through sheer traffic volume
- Problems in third party dependencies that are already public (please report those upstream)
