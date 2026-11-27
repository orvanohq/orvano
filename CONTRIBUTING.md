# Contributing to Orvano

Thanks for taking an interest. Orvano is **pre alpha**: the design is settled, but most of the code doesn't exist yet and things move fast. Small, focused pull requests are the easiest to review.

## Before you start

- For anything bigger than a small fix, open an issue first so we can agree on the approach.
- What Orvano should do is described in [`docs/design/spec.md`](docs/design/spec.md). The order of work is in [`docs/scope/`](docs/scope/), and the technical decisions are in [`docs/specs/`](docs/specs/). A change that goes against a spec needs the spec updated first.
- Conventions for each part of the repo are in the `AGENTS.md` files (the root one, plus one in `server/`, `cli/`, `console/`, `website/` and `deploy/`). They are written for people and coding agents alike.

## Set up

You need:

- the .NET 10 SDK (the version floor is in `global.json`)
- Node.js at the version in `.nvmrc`, and pnpm (`npm install -g pnpm`; it then switches to the version pinned in `package.json`)
- for the native CLI build, your platform's C toolchain: clang on Linux, the Xcode command line tools on macOS, or the MSVC build tools on Windows
- Docker, to run the stack and the integration tests

Then, from the repo root:

```bash
dotnet build Orvano.slnx
dotnet test --solution Orvano.slnx
pnpm install
pnpm -r build
```

To run Postgres, `orvano` and the executor together, start the contributor AppHost:

```bash
dotnet run --project server/src/Orvano.AppHost
```

## Making a change

1. Branch from `main` (for example `feat/storage-buckets` or `fix/token-expiry`).
2. Keep to the rules in the nearest `AGENTS.md`. The architecture tests in `server/tests/Orvano.ArchitectureTests` check which projects may reference which, so run the tests before you push.
3. Add or update tests. Integration tests run against a real Postgres through Testcontainers.
4. Write commit messages in the [Conventional Commits](https://www.conventionalcommits.org/) style, for example `feat(auth): add sign in with email`.
5. Sign off every commit (see below), push, and open a pull request against `main`.

## Sign off your commits (DCO)

Orvano uses the [Developer Certificate of Origin](https://developercertificate.org/) (DCO) instead of a contributor licence agreement. By adding a `Signed-off-by` line to a commit, you certify that you wrote the change, or otherwise have the right to submit it under the project's licences.

Git adds the line for you when you commit with `-s`:

```bash
git commit -s -m "fix(cli): print the version on --version"
```

It looks like this, and the email must match the commit's author email:

```
Signed-off-by: Your Name <you@example.com>
```

A check runs on every pull request and fails if a commit has no matching sign off. To fix the last commit, run `git commit --amend -s --no-edit`. To fix several, run `git rebase --signoff main`, then push with `git push --force-with-lease`.

## Licensing of contributions

Your contribution is licensed under the licence of the folder it changes: Apache-2.0 for the server and most of the repo, MIT for `cli/`, `server/src/Orvano.Wire/` and the SDKs. See [NOTICE](NOTICE) for details.

## Reporting security problems

Please don't open a public issue for a vulnerability. Follow [SECURITY.md](SECURITY.md) instead.
