# Review, feat/self-host-installer, 2026-09-27

**Reviewed by**: Claude Sonnet 5 (author on Claude Opus 5.5)
**Scope**: 68 files, branch vs main (merge base 5108efa)
**Verdict**: Approve

## Summary
This branch builds the self host installer end to end on the server side: the image only compose file with log rotation and Postgres tuning, `orvano install` and `install.sh`, the setup token gate that protects the first admin account, `consoleInstall.getSetup`, `setup-status`, the gateway's ACME email and HSTS, and the GHCR release workflow. The console `/setup` route is correctly left for a later branch, as the scope note says. This is careful, well tested work: the secret handling, the constant time token comparison, the atomic file writes, the compose rewrite versus the override file, and the CI install job all match the spec closely. I read every security sensitive path in detail (setup token compare and storage, `.env` permissions, `install.sh` locking and idempotency, the Caddy ACME email escaping, the GHCR workflow permissions) and did not find a bug worth blocking on.

## Minor
### 🟡 The `consoleInstall.getSetup` rate limit from AC-22 is not implemented yet, `server/src/Orvano.Platform/Endpoints/ConsoleEndpoints.cs:127`
**Problem**: AC-22 calls for `consoleInstall.getSetup` to be rate limited to 60 requests per minute per connection IP. The endpoint has no rate limiting attached, and there is no rate limiting code anywhere in the branch.
**Why it matters**: This is the one console route that answers with no session, so it is the one place a stranger can poll an install from outside without any other gate. The information it leaks is only a boolean, so the risk is low, but an unthrottled endpoint is still an easy target for reconnaissance or noise.
**Suggested fix**: Nothing to do in this branch. `docs/specs/0006-self-host-installer/verify.md` already lists this as owed and explicitly defers it to spec 0004 task 1, alongside the console `/setup` route. I list it here only so the deferral is visible in the review, not as a request to add it now.

## Nits
- ⚪ `deploy/install/install.sh:349`, `run_installer` forwards the full original `"$@"` to the container after already adding an explicit `--existing-data` and `--version`. When the caller passed `--version` themselves, the container sees `--version` twice with the same value; harmless since `InstallOptions.Parse` lets the later flag win, but worth a comment if a future flag's repeat isn't as harmless.
- ⚪ `server/src/Orvano.Server/Install/InstallOptions.cs:16`, `--dir`, `--timeout`, and `--no-pull` are accepted and silently ignored by the container parser, which is correct per the design (`install.sh` passes flags through untouched) but relies on a reader knowing that convention; the doc comment above the record already explains it, so this is just a note that it's easy to trip over when adding a new script only flag later.

## Strengths
- Every secret path was actually exercised: `InstallCommandTests` proves byte for byte `.env` stability across reruns, atomic temp-file-then-rename writes, correct Unix modes, and that no secret ever reaches `install.log` or stdout; `SetupTokenTests` covers the racing first sign up, the malformed token startup refusal in every environment, and the exact 401 versus 200 boundary on the one sessionless console route.
- The setup token is generated from `RandomNumberGenerator`, stored and compared with `CryptographicOperations.FixedTimeEquals`, and the `.env`/log/HTTP paths were all checked by hand for leaks; the gateway's ACME email is escaped correctly before being embedded in the Caddyfile.
- The GHCR release workflow keeps the existing dry run gate, adds only the job level permissions it needs (`packages: write` for images, `contents: write` for the release), and the CI install job (`ci.yml`) proves the real `install.sh` twice, on both architectures, byte comparing every secret between runs.

## Test coverage
Very thorough for a branch of this size: `EnvFileTests`, `InstallRulesTests` (domain, email, version, Postgres tuning, secret formats, DNS matching), `InstallPlanTests`, `InstallCommandTests` (fresh install, rerun stability, DNS warnings, domain change warning, downgrade refusal, terminal prompts), and `SetupTokenTests` (server side AC-19 to AC-22, AC-24) between them exercise essentially every acceptance criterion this branch claims. The one documented gap is the `consoleInstall.getSetup` rate limit (AC-22), already called out above and already tracked in `verify.md` as deferred, not a silent hole.
