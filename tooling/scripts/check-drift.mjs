// Spec 0008 CI-2, spec 0003 Enforcement: the console's generated files must match their sources.
//   I18N-3: the message catalogs match what `lingui extract` would write.
//   RTE-1: the committed route tree matches what the router plugin generates in a build.
//   QRY-1 (spec 0014 CTR-5): the console's API types match api/console.openapi.json.
import { spawnSync } from "node:child_process";

const isWindows = process.platform === "win32";

/**
 * Runs a command and stops the script with its exit code when it fails.
 * @param {string} command
 * @param {string[]} args
 */
function run(command, args) {
  const result = spawnSync(command, args, { stdio: "inherit", shell: isWindows });
  if (result.status !== 0) {
    console.error(`check-drift: \`${command} ${args.join(" ")}\` failed.`);
    process.exit(result.status ?? 1);
  }
}

run("pnpm", ["--filter", "@orvano/console", "exec", "lingui", "check", "sync", "--clean"]);
// vite build alone (no typecheck): the router plugin rewrites src/routeTree.gen.ts as it builds.
run("pnpm", ["--filter", "@orvano/console", "exec", "vite", "build", "--logLevel", "warn"]);
run("pnpm", ["--filter", "@orvano/console", "run", "api:gen"]);
run("git", [
  "diff",
  "--exit-code",
  "--",
  "console/src/routeTree.gen.ts",
  "console/src/shared/api/schema.gen.ts",
]);
console.log("check-drift: catalogs, route tree, and API types are in sync.");
