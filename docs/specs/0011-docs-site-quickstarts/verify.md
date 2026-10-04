# Verify: Docs site & quickstarts · spec 0011 · updated 2026-10-04 (milestone 5, the release)
_Steps derived from spec 0011 acceptance criteria. `/check verify` runs these; `/test` locks the durable ones. Covers milestones 1 (local stack), 2 (thin thread), 3 (remaining quickstarts and the generated reference), and 4 (content); later milestones append here._

## Commands
- [x] In an empty folder, `docker run --rm --user "$(id -u):$(id -g)" -v "$PWD:/install" ghcr.io/orvanohq/orvano:<V> install --local --yes` → exit 0. The folder now holds `.env` (0600, owned by you), `docker-compose.yml`, `docker-compose.local.yml`, and `initdb/`. The output lists, in this order, the up command, `http://localhost:7700`, the setup link, `http://localhost:8025`, the master key block, and the stop and reset commands → AC-1
- [x] Add `--domain localhost`, then `--email a@b.test`, then `--port 1023`, `--port 8025`, and `--port 65536` → each exits 2 and writes nothing → AC-1
- [x] `docker compose up -d --wait` in that folder → every service is healthy; `curl http://localhost:7700/v1/health` answers 200; `/` loads the console → AC-2
- [x] `docker compose ps --format json | jq -r '.Publishers[]? | select(.PublishedPort != 0) | "\(.URL):\(.PublishedPort)"' | sort -u` → exactly `127.0.0.1:7700` and `127.0.0.1:8025` → AC-2
- [x] Container and volume names start with `COMPOSE_PROJECT_NAME`, which is `orvano-local-` plus 6 hex characters; a second folder gets a different name → AC-2
- [x] Rerun the same `install --local` → `.env` is byte for byte the same, and the summary shows the backup reminder instead of the key → AC-3
- [x] Rerun with `--port 7701` and no `--yes` → exit 2, `.env` unchanged; with `--yes` → `ORVANO_PUBLIC_URL=http://localhost:7701` → AC-3
- [x] `install --local` on a server install folder (`--domain localhost`) → exit 2, and every file, `install.log` included, is unchanged → AC-3
- [x] `install --domain localhost --existing-data=yes`, and `install.sh --dir <local folder>`, on a local folder → both exit 2, `.env` unchanged → AC-3
- [x] On the local stack, the console SMTP row is `mailpit:1025`, security `none`, From `orvano@local.test`, `updated_by_user_id` all zeros → AC-4
- [x] Create the first admin from the setup link, create an org, and invite someone → the invite lands in Mailpit at `http://localhost:8025` within 30 seconds → AC-4
- [x] Change the install SMTP in the console, then restart `api` → the row keeps the console's values → AC-4
- [x] Start `api`, `worker`, and `realtime` with `ORVANO_INSTALL_SMTP_URL=smtp://user:secret@host` → each exits 1 with a message naming the setting; `secret` and `host` appear nowhere in the output → AC-4
- [x] `ORVANO_INSTALL_SMTP_URL=smtps://user:p%40ss@smtp.example.com` → the seeded row has port 465, security `tls`, username `user`, and a sealed password (not the plain text) → AC-4
- [x] `pnpm --filter @orvano/website build` without `ORVANO_SITE_ENV` → fails with a message asking for it; with `preview` → builds `website/dist/` with `/`, `/docs/`, `/errors/`, `/docs/api/`, the Pagefind index, the sitemap, and `404.html` → AC-5
- [x] A preview build has `<meta name="robots" content="noindex">` on every page; a production build with `PUBLIC_CF_BEACON_TOKEN` has the beacon script and no `noindex` → AC-5, AC-24
- [x] Rename a region in `examples/nextjs-quickstart/.env.example`, or break an internal link in an `.mdx` page → the site build fails → AC-9, AC-18
- [x] `node .github/scripts/check-example-pins.mjs` passes; change the `@orvano/nextjs` pin → it fails → AC-14
- [x] `ORVANO_LOCAL_DIR=<fresh local folder> pnpm --filter @orvano/website screenshots` → 14 PNGs (7 steps, light and dark) and `quickstart.env` with `ORVANO_ENDPOINT`, `ORVANO_PROJECT`, and an `orv_sk_` key → AC-10, AC-21
- [x] Copy `examples/nextjs-quickstart` outside the repo, point it at packed SDKs with `node .github/scripts/use-local-sdks.mjs`, write `.env.local` from `quickstart.env`, then `npm install && npm run build && npm start` → `pnpm --filter @orvano/website check:web-quickstart http://localhost:3000` passes → AC-11, AC-14, AC-19
- [x] On PR orvanohq/orvano#93, the `Website passed` checks run: `build`, plus `quickstarts` (because install, deploy, and examples changed); `preview` skips with a notice until the Cloudflare token exists → AC-18, AC-19, AC-26, AC-27

## Commands: remaining quickstarts and the generated reference (milestone 3)
- [x] `pnpm --filter @orvano/contract build` → `contract/dist/errors.json` lists 49 codes, each with `code`, `status`, `description`; it has `console_session_required` and `setup_token_invalid` and no `contract_violation` or `test_conflict`; a second build leaves git clean → AC-16
- [x] Remove the `(404)` from one `ErrorCode` `@doc`, then build the contract → it fails and names the member → AC-16
- [x] `pnpm --filter @orvano/website build` (preview) → `dist/errors/<code>/index.html` exists for every code in `errors.json`, `/errors/` lists them all, and `dist/errors/contract_violation/` does not exist → AC-17
- [x] Delete `src/content/error-fixes/user_not_found.mdx`, or add `not_a_code.mdx`, or drop its `## How to fix` heading → `node website/scripts/prepare.ts` fails naming the file → AC-17
- [x] `dist/docs/api/operations/` has one page per operation in `openapi.public.json` (37 today) and nothing under `/v1/console/` or `/v1/test/` (`grep -r "/v1/console/\|/v1/test/" website/dist/docs/api` finds nothing) → AC-15
- [x] Remove every link to a `users.*` operation from the guides → prepare fails with "No guide links to the API reference for the users tag"; link to `/docs/api/operations/usersfoo/` → prepare fails naming the bad link → AC-18
- [x] `node .github/scripts/check-example-pins.mjs` passes; change the `orvano_dart` pin to `^0.0.0`, or the `Orvano` `PackageReference` version, or the Android `applicationId` → it fails naming the file → AC-10, AC-14
- [x] `dotnet test --project tools/tests/Orvano.SdkGen.Tests` passes, including `Pins_each_example_to_exactly_the_version` for all five examples → AC-14
- [x] With a local stack and `quickstart.env` from `pnpm --filter @orvano/website screenshots`: copy `examples/js-quickstart` out of the repo, `node .github/scripts/use-local-sdks.mjs <copy> <packs>`, write `.env.local`, `npm install && npm run build && npm run preview` → `check:web-quickstart http://localhost:5173` passes → AC-11, AC-19
- [x] Copy `examples/dart-quickstart`, point it at the checkout, `dart run bin/create_user.dart a@example.com 'a long password' 'Ada'`, `dart run bin/server.dart` → `check:server-quickstart http://localhost:3001 a@example.com 'a long password'` passes (200 with the user, 401 with no token and with a bad token) → AC-12, AC-19
- [x] Same for `examples/dotnet-quickstart` with `dotnet run -- create-user ...` and `dotnet run`, on port 3002 → AC-12, AC-19
- [x] Copy `examples/flutter-quickstart`, point it at the checkout, run chromedriver on 4444, then `flutter drive -d web-server --browser-name=chrome --web-port 5050 ... --dart-define=ORVANO_PROJECT=<id>` → All tests passed, with no `--disable-web-security` → AC-11, AC-19
- [x] On a Mac, `flutter test integration_test -d <iPhone simulator> --dart-define=ORVANO_PROJECT=<id>` against the local stack → passes → AC-11, AC-20
- [x] On PR orvanohq/orvano#93, `Quickstarts against a local stack` runs all five quickstarts and passes; a manual run of `SDKs nightly` passes `Scenarios, Flutter on iOS` (with the quickstart step) and `Flutter quickstart on Android` → AC-19, AC-20

## UI / manual
- [x] Open `/` → the tagline, the macOS and Linux and PowerShell tabs with the three commands at the exact version, five quickstart cards, the four differentiators each marked Planned, and a GitHub link → AC-5, AC-8, AC-13
- [x] The sidebar shows Get started, Concepts, Auth guides, SDKs, Console, Self hosting, API reference, Errors, and Changelog, in that order, with every page AC-6 names → AC-6
- [x] Switch the theme to light and dark → the colors follow the console's indigo violet, Inter for text and JetBrains Mono for code, and each screenshot swaps to the matching theme → AC-7, AC-21
- [x] Every page header shows `v<VERSION>`; every image and install command uses that version, never `latest` → AC-8
- [x] The Next.js quickstart walks setup, project, project ID, and the Web platform `localhost` with screenshots, then shows the files and what you should see after each try step → AC-10, AC-11

## UI / manual: milestone 3
- [x] Open `/errors/user_blocked/` → the status 403, the description, "Why it happens", "How to fix", and a tab each for JavaScript and Next.js, Dart and Flutter, and .NET matching on `ErrorCode.userBlocked` / `ErrorCode.UserBlocked` → AC-17
- [x] Open `/docs/api/operations/usersget/` → method, path, parameters, response schema, the default problem response, "Server SDKs", "API key scope: `users.read`", and tabs JavaScript, Dart server, .NET; `/docs/api/operations/accountcreate/` shows "Client SDKs" and tabs JavaScript, Next.js, Flutter → AC-15
- [x] The sidebar's API reference group shows the operations grouped by tag (account, health, keys, users) → AC-15
- [x] The JavaScript, Flutter, Dart, and .NET quickstart pages each start from the three local commands, walk the console steps with screenshots (Flutter: Web, iOS, Android tabs; Dart and .NET: the API key), and show only code pulled from their example → AC-9, AC-10, AC-13
- [x] The Flutter page shows `adb reverse tcp:7700 tcp:7700`, the debug only `network_security_config.xml` for `localhost`, the iOS `NSAllowsLocalNetworking` key, and `flutter run -d chrome --web-port 5050` → AC-11
- [x] The Dart and .NET pages show the settings and the token call in macOS and Linux and Windows PowerShell tabs, and the API key only as an environment variable → AC-12, AC-13

## Commands: content (milestone 4)
- [x] `ORVANO_SITE_ENV=preview pnpm --filter @orvano/website build` → passes, "All internal links are valid", and "Wrote a Markdown copy of" about 126 pages → AC-6, AC-18
- [x] `grep -rl "being written for this release" website/src/content` → finds nothing: every page AC-6 names has content → AC-6
- [x] `website/dist/llms.txt` names Orvano, says how to get a page's `.md` copy, and links `llms-full.txt` and `llms-small.txt`; `llms-full.txt` holds the quickstarts' code (`grep -c createServerClient` finds it) → AC-5
- [x] `website/dist/docs/local.md`, `dist/docs/auth/email-verification.md`, `dist/docs/api/operations/usersget.md`, and `dist/errors/user_blocked.md` exist, start with the page title, and show code as fenced blocks with tab labels in bold → AC-5
- [x] Add a line under `## [Unreleased]` in `CHANGELOG.md`, rebuild → `/docs/changelog/` shows it; `website/src/content/docs/docs/changelog.md` is generated and gitignored → AC-28
- [x] On a fresh local stack, `ORVANO_LOCAL_DIR=<folder> pnpm --filter @orvano/website screenshots` → 24 PNGs: the 14 quickstart ones plus `email-settings`, `email-templates`, `email-template-editor`, `invite-member`, and `install-email-server`, each light and dark, with no toast covering a page; `SCREENSHOTS=0` skips the new steps → AC-21

## UI / manual: milestone 4
- [x] Every page in Concepts, Auth guides, SDKs, Console, and Self hosting has real content, in the order AC-6 lists, and Changelog renders `CHANGELOG.md` → AC-6, AC-28
- [x] Each auth guide has a tab per SDK: JavaScript, Next.js, and Flutter for the user's own account; JavaScript, Dart, and .NET for the server parts; the tab choice sticks across pages → AC-29
- [x] Email verification shows the landing page pattern (a Continue button that posts through `redeemLink` or `handleLink`, and `Referrer-Policy: no-referrer`), gating on `emailVerified` in the app and on the server, and Flutter deep links with https app links, saying that part can't run against a local stack → AC-29
- [x] Each email guide (verification, password reset, magic links, email codes, change email) starts from a finished quickstart and the local inbox at `http://localhost:8025` → AC-29
- [x] On a local stack with the Next.js quickstart, follow Email verification: the email shows in Mailpit with a Verify email button; Continue on `/auth/confirm` verifies the user, and the console's Users page shows them verified → AC-29
- [x] The console pages show their screenshots in the reader's theme, each with alt text that describes the step → AC-21
- [x] Install on a server and Upgrade and repair hold everything the README's install section said, with the install command at `releases/download/v<VERSION>/install.sh` (never `latest`); the README keeps a two line summary that links to them → AC-8, AC-28
- [x] `CONTRIBUTING.md` says a pull request that adds a public operation, an error code, or a console screen updates its guide, concept, or console page in the same pull request, and which of those the build enforces → AC-30

## Value sourcing
- [x] Port: no flag on a fresh folder gives 7700; on a rerun with no flag it keeps the `.env` port; `--port` wins → local install port
- [x] Setup link: the token in the printed link equals `ORVANO_SETUP_TOKEN` in `.env` → printed setup link
- [x] Project name: generated on the first run, then kept byte for byte on every rerun → compose project name
- [x] SMTP seed: `smtp://`, `smtp+starttls://`, and `smtps://` give security `none`, `starttls`, and `tls`, with default ports 25, 587, and 465 → seed host, port, and security
- [x] Docs version: change `VERSION`, rebuild, and the badge and every command follow it; SdkGen moves the example pin to match → documented version, example package versions
- [x] Example settings: the app reads `NEXT_PUBLIC_ORVANO_ENDPOINT` and `NEXT_PUBLIC_ORVANO_PROJECT` only, and refuses to start without the project → example endpoint and project
- [x] Error page contents: change a member's `@doc` in `contract/errors.tsp`, rebuild the contract and the site → that error page's description and status follow → error page code, status, description
- [x] SDK snippets: the tabs on an operation page match `contract/dist/examples/<sdk>/<operationId>.*` byte for byte, without the generated header → API reference SDK snippets
- [x] Audience and scope: an operation's labels follow its `x-orvano-audience` and `x-orvano-scope` in `openapi.public.json` → API reference audience, scope
- [x] Example settings: the JS app refuses to start without `VITE_ORVANO_PROJECT`; the Flutter app without `--dart-define=ORVANO_PROJECT`; the Dart and .NET servers without `ORVANO_PROJECT` or `ORVANO_API_KEY` → example endpoint and project
- [x] Dev ports: Vite 5173, Flutter web 5050, Dart 3001, .NET 3002 → example dev ports
- [x] App IDs: the Flutter iOS bundle ID and Android `applicationId` equal `appId` in `website/scripts/screenshots.ts` → iOS bundle ID, Android package name

- [x] Install command: change `VERSION`, rebuild → Install on a server, Upgrade and repair, and every SDK install command follow it (`releases/download/v<new>/install.sh`, `@orvano/js@<new>`) → install and image command version
- [x] Changelog entries: the Changelog page shows `CHANGELOG.md` and nothing else → changelog page entries

## Release (milestone 5, v0.2.0 on 2026-10-04)
- [x] Push `v0.2.0` → `release.yml` publishes npm, NuGet, and the images, skips pub.dev (uploaded by hand first), and `Examples from the registries` builds all five quickstarts from npm, pub.dev, and NuGet at 0.2.0 before `Deploy orvano.dev` waits for approval in `website-production` → AC-25
- [x] `https://orvano.dev/docs/` answers 200 with `X-Content-Type-Options: nosniff`, `Referrer-Policy: strict-origin-when-cross-origin`, HSTS, and the CSP with its script hashes; the page shows `v0.2.0`, loads the beacon, and has no `noindex` → AC-23, AC-24
- [x] `https://www.orvano.dev/docs/` answers 301 to `https://orvano.dev/docs/`; an unknown path and an unknown `/errors/<code>` answer 404 with the 404 page, the second linking to `/errors/` → AC-23
- [x] With no GitHub credentials, `docker run ghcr.io/orvanohq/orvano:0.2.0 install --local --yes` then `docker compose up -d --wait` → healthy, `/v1/health` answers version 0.2.0 → AC-1, AC-2, AC-8
- [x] A manual run of `links.yml` checks every external link (0 errors on 2026-10-04, so no issue opened); a dead link opens or updates its one issue, and a clean run closes it → AC-22

## Acceptance-criteria coverage
- AC-1 to AC-4: the Commands steps above, plus `LocalInstallTests`, `InstallSmtpSeedRuleTests`, and `InstallSmtpSeedTests`
- AC-5 to AC-11, AC-13, AC-14, AC-18, AC-19, AC-21, AC-24 (noindex part), AC-26, AC-27: the steps above
- AC-9 to AC-12, AC-14 to AC-20: the milestone 3 steps above, plus `ManifestStamperTests`
- AC-5 (llms.txt, Markdown copies), AC-6, AC-21, AC-28 to AC-30: the milestone 4 steps above
- AC-22, AC-23, AC-25: the release steps above
