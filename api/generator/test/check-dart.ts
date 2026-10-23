// Spec 0014 AC-14, GNR-10, spec 0004 DQA-5: the Dart snapshots pass the Dart SDK repos' analyzer, and the shared Dart
// runtime passes its unit tests (snapshots/dart-server/test/core/). Needs a Dart SDK on PATH; CI uses the floor (DRT-1).
//   pnpm test:generator:dart
import { spawnSync } from "node:child_process";
import { writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";

const snapshots = fileURLToPath(new URL("snapshots/", import.meta.url));

// Written here, not committed: GitHub's dependency graph has no SPDX license data for pub packages, so a committed
// pubspec.yaml fails the core repo's license gate (spec 0009 DEP-8). The SDK repos' own pubspecs carry the real
// constraints (spec 0004 DRT-2, DRT-3).
for (const [directory, name] of [
  ["dart-client", "orvano"],
  ["dart-server", "orvano_server"],
] as const) {
  writeFileSync(
    `${snapshots}${directory}/pubspec.yaml`,
    `# Written by check-dart.ts: a harness around the ${directory} snapshot.
name: ${name}
publish_to: none

environment:
  sdk: ^3.10.0

dependencies:
  clock: ^1.1.0
  collection: ^1.18.0
  http: ^1.5.0
  logging: ^1.2.0
  meta: ^1.15.0

dev_dependencies:
  fake_async: ^1.3.0
  lints: ^6.0.0
  test: ^1.25.0
`,
  );
}
const steps: [string, string[]][] = [
  ["dart-client", ["pub", "get"]],
  ["dart-client", ["analyze", "--fatal-infos"]],
  ["dart-server", ["pub", "get"]],
  ["dart-server", ["analyze", "--fatal-infos"]],
  ["dart-server", ["test", "test/core"]],
];

for (const [directory, args] of steps) {
  console.log(`\n${directory}: dart ${args.join(" ")}`);
  const result = spawnSync("dart", args, {
    cwd: `${snapshots}${directory}`,
    stdio: "inherit",
    shell: process.platform === "win32",
  });
  if (result.status !== 0) {
    console.error(`check-dart: dart ${args.join(" ")} failed in snapshots/${directory}.`);
    process.exit(result.status ?? 1);
  }
}
