// Spec 0014 AC-14, GNR-9, spec 0002 LIB-11: the csharp-server snapshot builds with sdk-dotnet's compiler settings
// (warnings as errors, trim and AOT analyzers, every public member documented) against a stub of the hand written
// runtime, and is already clean under sdk-dotnet's `dotnet format`, so regenerating there changes nothing. Needs the
// .NET SDK from server/global.json on PATH.
//   pnpm test:generator:csharp
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const harness = fileURLToPath(new URL("snapshots/csharp-server/", import.meta.url));
const steps: string[][] = [
  ["build", "csharp-server.slnx", "-c", "Release", "--no-incremental"],
  ["format", "whitespace", "csharp-server.slnx", "--verify-no-changes", "--include-generated"],
  ["format", "style", "csharp-server.slnx", "--verify-no-changes", "--include-generated"],
];

for (const args of steps) {
  console.log(`\ncsharp-server: dotnet ${args.join(" ")}`);
  const result = spawnSync("dotnet", args, {
    cwd: harness,
    stdio: "inherit",
    shell: process.platform === "win32",
  });
  if (result.status !== 0) {
    console.error(`check-csharp: dotnet ${args.join(" ")} failed in snapshots/csharp-server.`);
    process.exit(result.status ?? 1);
  }
}
