import { defineConfig } from "vitest/config";

// Spec 0014 GNR-10: the generator's snapshot and bad input tests, run in Node.
export default defineConfig({
  test: { include: ["test/*.test.ts"], environment: "node" },
});
