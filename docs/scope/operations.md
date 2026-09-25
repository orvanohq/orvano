# Epic: Backups & environments ★

Orvano's fourth differentiator. Appwrite keeps automatic backups for its paid cloud; self hosted Supabase has no branching and runs one project per stack. Orvano gives self hosters both, free. See [index.md](index.md) for the full plan.

### 35. Backups & point in time restore ★ · needs a decision · GA
Scheduled backups of databases, files, and project config to any S3 compatible target you choose, with retention rules and restore to a point in time or into a new project.
**Done when:** a daily backup runs on schedule to your storage, and restoring to a chosen time recovers deleted rows and files without touching other projects.
- [ ] Design it (spec): `/architect backups & point in time restore`

### 36. Environments & schema promotion ★ · needs a decision · GA
Dev, staging, and production environments inside one project, with migrations and config promoted from one to the next, and project settings kept as code that the CLI can diff and apply.
**Done when:** a schema change made in dev is promoted to staging then production through the CLI or console, with a diff shown before applying and no data copied by accident.
- [ ] Design it (spec): `/architect environments & schema promotion`
