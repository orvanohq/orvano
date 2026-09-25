# Epic: Functions

Server code you deploy to Orvano, with Dart, .NET, and Node runtimes first so every server SDK is used inside functions. See [index.md](index.md) for the full plan.

### 27. Functions deploy & execute · needs a decision · GA
Deploy a function from the CLI in Dart, .NET, or Node; call it over HTTP or from any SDK; set environment variables and secrets; see builds, runs, and logs in the console. Runs untrusted code, so isolation is load bearing.
**Done when:** a function in each runtime deploys from the CLI, runs when called from Flutter and Next.js, reads a secret, and shows its logs and duration in the console; one function cannot reach another project's data.
- [ ] Design it (spec): `/architect functions deploy & execute`

### 28. Function triggers & Git deploys · needs a decision
Run functions on events (user created, row changed, file uploaded), on a schedule, and deploy automatically from a Git repository on push.
**Done when:** a function fires on a database event and on a schedule, and pushing to a connected repo deploys a new version with its build log.
- [ ] Design it (spec): `/architect function triggers & Git deploys`
