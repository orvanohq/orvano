import { readFileSync } from "node:fs";
import { ORG_ID, PROJECT_ID } from "./mocks";

/** Every route the build has, read from the generated route tree, so a new route joins each test by itself. */
export function routePaths(): string[] {
  const tree = readFileSync(new URL("../src/routeTree.gen.ts", import.meta.url), "utf8");
  const union = /fullPaths:\s*([^\n]+(?:\n\s*\|[^\n]+)*)/.exec(tree)?.[1] ?? "";
  const paths = [...union.matchAll(/'([^']+)'/g)].map((match) => match[1] ?? "");
  return [...new Set(paths.map((path) => (path === "/" ? path : path.replace(/\/$/, ""))))];
}

/** A route with sample values for its params. */
export function urlOf(path: string): string {
  return path.replace("$orgId", ORG_ID).replace("$projectId", PROJECT_ID);
}

/** Where a route ends up, after the redirects of `/` and of the project settings. */
export function landingOf(path: string): string {
  if (path === "/") return `/${ORG_ID}`;
  const url = urlOf(path);
  return path === "/$orgId/$projectId/settings" ? `${url}/general` : url;
}
