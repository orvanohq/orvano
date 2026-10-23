// Spec 0014 GNR-9: generate --target <target> --contract <path> --out <repo root> [--commit <40 hex>].
// Relative paths resolve from where pnpm was run (INIT_CWD), not from this package.
import { execFileSync } from "node:child_process";
import { existsSync, statSync } from "node:fs";
import { resolve } from "node:path";
import { parseArgs } from "node:util";
import { fileURLToPath } from "node:url";
import { GeneratorError, loadContract } from "./contract.ts";
import { generate } from "./generate.ts";
import { writeOutput } from "./output.ts";
import { findTarget, targetNames } from "./targets/index.ts";

const usage = `Usage: generate --target <${targetNames.join("|")}> --contract <openapi.json> --out <repo root> [--commit <sha>]`;

function main(): void {
  const { values } = parseArgs({
    options: {
      target: { type: "string" },
      contract: { type: "string" },
      out: { type: "string" },
      commit: { type: "string" },
    },
    strict: true,
  });
  if (values.target === undefined || values.contract === undefined || values.out === undefined) {
    throw new GeneratorError(usage);
  }
  const base = process.env["INIT_CWD"] ?? process.cwd();
  const out = resolve(base, values.out);
  if (!existsSync(out) || !statSync(out).isDirectory())
    throw new GeneratorError(`--out ${out} is not a directory.`);
  const commit = values.commit ?? currentCommit();
  if (!/^[0-9a-f]{40}$/.test(commit))
    throw new GeneratorError(`--commit must be 40 lowercase hex, got ${commit}.`);

  const target = findTarget(values.target);
  const files = generate(loadContract(resolve(base, values.contract)), target.name, commit);
  writeOutput(out, target.ownership, files);
  console.log(`sdk-generator: wrote ${String(files.length)} files for ${target.name} into ${out}.`);
}

/** The orvano commit this generator runs from (GNR-8). */
function currentCommit(): string {
  const directory = fileURLToPath(new URL(".", import.meta.url));
  return execFileSync("git", ["-C", directory, "rev-parse", "HEAD"], { encoding: "utf8" }).trim();
}

try {
  main();
} catch (error) {
  if (!(error instanceof GeneratorError)) throw error;
  console.error(`sdk-generator: ${error.message}`);
  process.exitCode = 1;
}
