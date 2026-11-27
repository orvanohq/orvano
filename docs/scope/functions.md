# Scope: Functions

User code in Node.js, Dart and .NET, built and run in sandboxed containers by the executor. Part of the [Orvano scope](index.md).

## M5 (Phase 2: Core services)

### 54. Function sandbox & executor · needs a decision · Beta
The boundary around untrusted code, for both builds and runs. Sites reuse it in M8. A gap here hands over the server, so it gets the stricter tier.
**Done when:** the executor builds and runs each function in its own container through the restricted socket proxy, never the raw socket; containers have CPU and memory limits, a read only root filesystem, no route to the platform network and an egress allowlist; warm containers are reused; gVisor can be turned on as extra hardening; tests prove a function cannot reach Postgres or the host.
- [ ] Design it (spec): `/architect function sandbox & executor`

### 55. Function runtimes & builds · needs a decision
The three runtimes and the build path that turns source into a runnable image.
**Done when:** Node.js 22, Dart 3 and .NET 10 each have a base image, a small HTTP shim and a starter template; the worker queues a build, the executor builds and stores the image, and build logs stream live.
- [ ] Design it (spec): `/architect function runtimes & builds`

### 56. Function deployments & secrets · needs a decision
Deploy a function, keep every version, and give it its secrets and limits.
**Done when:** a function deploys from `orvano functions deploy` or a zip upload; every deployment is kept and switching the active one is an instant rollback; project secrets reach the function as environment variables; the timeout (15 seconds by default, up to 5 minutes), memory and concurrency caps are enforced.
- [ ] Design it (spec): `/architect function deployments & secrets`

### 57. HTTP triggers & direct calls · needs a decision
Call a function over HTTP or from an SDK, with the context it needs.
**Done when:** a function answers at `/fn/<project>/<function>` and from SDK calls; it receives the request, the calling user if there is one, and a short lived server key scoped to the project; its structured logs are captured.
- [ ] Design it (spec): `/architect http triggers & direct calls`

### 58. Event & cron triggers · needs a decision
Run a function when something happens or on a schedule.
**Done when:** a function runs on events from the catalog (such as `db.orders.created`) and on cron schedules; each run is recorded with its outcome.
- [ ] Design it (spec): `/architect event & cron triggers`

### 60. GitHub App & push deploys · needs a decision
Deploy on push, through a GitHub App that belongs to this instance. Sites reuse it in M8.
**Done when:** the console wizard creates a GitHub App for this instance through the app manifest flow; a repo, branch and folder can be linked to a function; a push builds and deploys it; an instance GitHub cannot reach is pointed at CLI deploys from CI instead.
- [ ] Design it (spec): `/architect github app & push deploys`

### 61. Functions console · needs a decision
The console pages for running and watching functions.
**Done when:** the console covers the function list, deployments, run history, logs, an invoke panel, triggers, secrets and the GitHub link.
- [ ] Design it (spec): `/architect functions console`

## Deferred
- **Python and more runtimes**: beyond Node.js, Dart and .NET
- **GitLab and Bitbucket deploys**: beyond GitHub
- **Long running jobs**: work past the 5 minute cap
- **GPUs**: functions with graphics cards
- **Workflows**: several steps chained with state
