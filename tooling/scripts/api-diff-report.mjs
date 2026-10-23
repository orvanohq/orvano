// Spec 0014 BRK-1 to BRK-3: turns the oasdiff reports of the API diff workflow into one PR comment, updated in place,
// and adds or removes the breaking-api label. It reports only: it exits 0 whatever oasdiff found (BRK-2, before beta).
// Usage (CI): node tooling/scripts/api-diff-report.mjs <public report> <console report>
// Env: PUBLIC_OUTCOME and CONSOLE_OUTCOME (the oasdiff steps' outcomes: failure means ERR findings or a failed run),
// and, to post, POST=true with GH_TOKEN, REPO, and PR_NUMBER. Without POST it writes only the job summary.
import { spawnSync } from "node:child_process";
import { appendFileSync, existsSync, readFileSync } from "node:fs";
import { resolve } from "node:path";

export const marker = "<!-- orvano-api-diff -->";
export const label = "breaking-api";

/**
 * What one oasdiff run found. `breaking` means ERR findings (the definition of breaking, BRK-1).
 * @param {string} report the text report, empty when the run wrote none
 * @param {string} outcome the step outcome: success, failure, or skipped
 * @returns {{ state: "none" | "changes" | "breaking" | "failed", report: string }}
 */
export function classify(report, outcome) {
  const text = report.trim();
  const errors = Number(/(\d+) errors?\b/.exec(text)?.[1] ?? 0);
  if (outcome === "failure")
    return errors > 0 ? { state: "breaking", report: text } : { state: "failed", report: text };
  if (text === "" || /^No changes detected/m.test(text)) return { state: "none", report: text };
  return { state: errors > 0 ? "breaking" : "changes", report: text };
}

const headline = {
  none: "no breaking changes.",
  changes: "possible breaking changes (warnings only).",
  breaking: "**breaking changes.**",
  failed: "the diff could not run; see the job log.",
};

/**
 * @param {string} title
 * @param {{ state: keyof typeof headline, report: string }} result
 */
function section(title, result) {
  const lines = [`**${title}**: ${headline[result.state]}`];
  if (result.state !== "none" && result.report !== "")
    lines.push("", "```text", result.report, "```");
  return lines.join("\n");
}

/**
 * @param {{ state: keyof typeof headline, report: string }} publicResult
 * @param {{ state: keyof typeof headline, report: string }} consoleResult
 */
export function commentBody(publicResult, consoleResult) {
  return [
    marker,
    "### API contract diff",
    "",
    section("Public API (`api/openapi.json`)", publicResult),
    "",
    section("Console API (`api/console.openapi.json`, report only)", consoleResult),
    "",
    "<sub>oasdiff against the base branch. Before beta this never fails CI (spec 0014 BRK-2); a breaking change " +
      "from beta on needs an `ApiVersions.Current` bump.</sub>",
    "",
  ].join("\n");
}

/**
 * @param {string[]} args
 * @param {string} [input]
 */
function gh(args, input) {
  const result = spawnSync("gh", args, { encoding: "utf8", input });
  if (result.status !== 0)
    throw new Error(`gh ${args.slice(0, 3).join(" ")} failed: ${result.stderr.trim()}`);
  return result.stdout;
}

/**
 * Creates or updates the one marked comment, and sets the label to match.
 * @param {string} body
 * @param {boolean} breaking
 */
function post(body, breaking) {
  const repo = process.env.REPO;
  const pr = process.env.PR_NUMBER;
  if (!repo || !/^[\w.-]+\/[\w.-]+$/.test(repo) || !pr || !/^\d+$/.test(pr)) {
    throw new Error("REPO and PR_NUMBER must be set to post.");
  }
  /** @type {{ id: number, body: string }[]} */
  const comments = JSON.parse(
    gh(["api", "--paginate", "--slurp", `repos/${repo}/issues/${pr}/comments`]),
  ).flat();
  const existing = comments.find((comment) => comment.body?.startsWith(marker));
  const payload = JSON.stringify({ body });
  if (existing)
    gh(
      ["api", "--method", "PATCH", `repos/${repo}/issues/comments/${existing.id}`, "--input", "-"],
      payload,
    );
  else
    gh(["api", "--method", "POST", `repos/${repo}/issues/${pr}/comments`, "--input", "-"], payload);

  if (breaking)
    gh(["api", "--method", "POST", `repos/${repo}/issues/${pr}/labels`, "-f", `labels[]=${label}`]);
  else spawnSync("gh", ["api", "--method", "DELETE", `repos/${repo}/issues/${pr}/labels/${label}`]); // absent is fine
}

/** @param {string | undefined} path */
const read = (path) => (path && existsSync(path) ? readFileSync(path, "utf8") : "");

if (process.argv[1] && resolve(process.argv[1]) === import.meta.filename) {
  const publicResult = classify(read(process.argv[2]), process.env.PUBLIC_OUTCOME ?? "success");
  const consoleResult = classify(read(process.argv[3]), process.env.CONSOLE_OUTCOME ?? "success");
  const body = commentBody(publicResult, consoleResult);
  if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, body);
  console.log(body);
  if (process.env.POST === "true") {
    try {
      post(body, publicResult.state === "breaking");
    } catch (error) {
      // BRK-2: the report never fails CI; the summary above still has it.
      console.log(
        `::warning::api-diff: could not update the PR comment or label: ${String(error)}`,
      );
    }
  }
}
