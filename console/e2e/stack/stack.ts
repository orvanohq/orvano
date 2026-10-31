import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const composeFile = fileURLToPath(new URL("compose.yaml", import.meta.url));

/** Runs `docker compose` on the stack's file and returns its standard output. */
export function compose(...args: string[]): string {
  return execFileSync("docker", ["compose", "-f", composeFile, ...args], {
    encoding: "utf8",
    stdio: ["ignore", "pipe", "inherit"],
  });
}
