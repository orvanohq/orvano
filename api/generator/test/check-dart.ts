// Spec 0014 AC-14, GNR-10, spec 0004 DQA-5: the Dart snapshots pass the Dart SDK repos' analyzer, and the shared Dart
// runtime passes its unit tests (snapshots/dart-server/test/core/). Needs a Dart SDK on PATH; CI uses the floor (DRT-1).
//   pnpm test:generator:dart
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const snapshots = fileURLToPath(new URL("snapshots/", import.meta.url));
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
