# Epic: Jobs, queues & cron ★

Orvano's third differentiator: durable background work as a first class product, not just a function on a timer. See [index.md](index.md) for the full plan.

### 33. Background jobs & queues ★ · needs a decision
Enqueue jobs from any SDK or function, process them with functions, with retries, delays, concurrency limits, priorities, and a dead letter queue. A console view of every queue and job.
**Done when:** a job enqueued from Next.js runs in a function, a failing job retries then lands in the dead letter queue, and you can inspect and retry it from the console.
- [ ] Design it (spec): `/architect background jobs & queues`

### 34. Scheduled jobs ★
Cron style schedules that enqueue jobs, with run history, pause and resume, and missed run handling.
**Done when:** a schedule runs on time, its history shows every run and result, and pausing it stops new runs.
- [ ] Build it: `/develop scheduled jobs`
