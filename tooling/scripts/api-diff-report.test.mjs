// Spec 0014 AC-19, BRK-1 to BRK-3: the API diff comment says what oasdiff found, and only ERR findings count as
// breaking (the label). The script itself never fails the job.
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { describe, test } from "node:test";

import { classify, commentBody, marker } from "./api-diff-report.mjs";

const breakingReport = `1 changes: 1 error, 0 warning, 0 info
error\t[response-required-property-removed] at api/openapi.json
\tin API GET /v1/health
\t\tremoved the required property \`apiVersions\` from the response with the \`200\` status`;

describe("classify", () => {
  test("ERR findings are breaking", () => {
    assert.equal(classify(breakingReport, "failure").state, "breaking");
  });

  test("warnings only are changes, not breaking", () => {
    assert.equal(
      classify("1 changes: 0 error, 1 warning, 0 info\nwarning\t[x]", "success").state,
      "changes",
    );
  });

  test("no changes, or no report, is none", () => {
    assert.equal(classify("No changes detected\n", "success").state, "none");
    assert.equal(classify("No breaking changes\n", "success").state, "none"); // the action's output file
    assert.equal(classify("", "success").state, "none");
  });

  test("a failed run without findings is failed, not breaking", () => {
    assert.equal(classify("Error: failed to load base spec", "failure").state, "failed");
  });
});

describe("commentBody", () => {
  test("starts with the marker and quotes the public findings", () => {
    const body = commentBody(classify(breakingReport, "failure"), classify("", "success"));
    assert.ok(body.startsWith(marker));
    assert.match(body, /\*\*Public API \(`api\/openapi\.json`\)\*\*: \*\*breaking changes\.\*\*/);
    assert.match(body, /removed the required property `apiVersions`/);
    assert.match(body, /\*\*Console API .*\*\*: no breaking changes\./);
  });
});

describe("the script", () => {
  test("writes the job summary and exits 0 without posting, even on breaking changes", () => {
    const dir = mkdtempSync(join(tmpdir(), "orvano-api-diff-"));
    try {
      const report = join(dir, "public.txt");
      const summary = join(dir, "summary.md");
      writeFileSync(report, breakingReport);
      writeFileSync(summary, "");
      /** @type {NodeJS.ProcessEnv} */
      const env = { ...process.env, PUBLIC_OUTCOME: "failure", GITHUB_STEP_SUMMARY: summary };
      delete env.POST;
      const result = spawnSync(
        process.execPath,
        [join(import.meta.dirname, "api-diff-report.mjs"), report, join(dir, "missing.txt")],
        { encoding: "utf8", env },
      );
      assert.equal(result.status, 0, result.stderr);
      assert.match(result.stdout, /breaking changes/);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
});
