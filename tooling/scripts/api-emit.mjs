// Spec 0014 CTR-3: `pnpm api:emit` rewrites api/openapi.json and api/console.openapi.json from the real host. It runs
// ContractEmitTests in write mode (ORVANO_CONTRACT_WRITE=1, set only here); every other test run compares instead,
// which is the CI drift check (CTR-5). Needs Docker or Podman, like the rest of the integration tests.
import { spawnSync } from "node:child_process";
import { resolve } from "node:path";

const result = spawnSync(
  "dotnet",
  [
    "test",
    "--project",
    "tests/Orvano.Host.IntegrationTests",
    "--filter-class",
    "Orvano.Host.IntegrationTests.Contract.ContractEmitTests",
  ],
  {
    stdio: "inherit",
    cwd: resolve("server"),
    env: { ...process.env, ORVANO_CONTRACT_WRITE: "1" },
    shell: process.platform === "win32",
  },
);
if (result.status !== 0) {
  console.error("api:emit: the emit run failed; api/ may be partly written.");
  process.exit(result.status ?? 1);
}
console.log("api:emit: wrote api/openapi.json and api/console.openapi.json.");
