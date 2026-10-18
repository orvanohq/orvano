// Spec 0010 SET-1 to SET-4, RUL-1 to RUL-3, ISS-4, ISS-5, ORG-1: GitHub settings as code. Check mode (the default)
// is read only: it prints every field that differs from the profile and exits 1 on any difference. --apply writes
// the differences with your own `gh` login, then checks again. Never run in CI (no workflow gets an admin token).
// Usage:
//   node tooling/scripts/github-settings.mjs <owner/repo> --profile <name> [--apply]
//   node tooling/scripts/github-settings.mjs --org <org>
// Exit codes: 0 in step, 1 drift, 2 usage or gh errors.
import { spawnSync } from "node:child_process";
import { resolve } from "node:path";

/** The GitHub Actions app: a required check only counts when this app reports it (RUL-3). */
export const actionsAppId = 15368;

/**
 * @typedef {{ name: string, color: string, description: string }} Label
 * @typedef {{
 *   name: string,
 *   repo: Record<string, boolean | string>,
 *   security: { secretScanning: boolean, pushProtection: boolean, dependabotAlerts: boolean,
 *     dependabotSecurityUpdates: boolean, privateVulnerabilityReporting: boolean },
 *   ruleset: Record<string, unknown>,
 *   labels: Label[],
 *   actions?: { enabled: boolean },
 *   discussionCategories?: { name: string, isAnswerable: boolean }[],
 * }} Profile
 * @typedef {{ field: string, want: unknown, got: unknown, waiting?: boolean, manual?: string }} Difference
 * @typedef {{ status: number, data: any }} ApiResponse
 * @typedef {(method: string, path: string, body?: unknown) => ApiResponse} Api
 */

/** SET-1: every profile. */
const baseRepo = {
  allow_squash_merge: true,
  allow_merge_commit: false,
  allow_rebase_merge: false,
  squash_merge_commit_title: "PR_TITLE",
  squash_merge_commit_message: "COMMIT_MESSAGES",
  web_commit_signoff_required: true,
  delete_branch_on_merge: true,
  allow_auto_merge: true,
  allow_update_branch: true,
  has_wiki: false,
  has_projects: false,
};

/** SET-2: every profile. */
const security = {
  secretScanning: true,
  pushProtection: true,
  dependabotAlerts: true,
  dependabotSecurityUpdates: true,
  privateVulnerabilityReporting: true,
};

/** ISS-5: in every repo with issues. */
const labels = [
  { name: "bug", color: "d73a4a", description: "Something isn't working" },
  { name: "enhancement", color: "a2eeef", description: "New feature or request" },
  { name: "triage", color: "fbca04", description: "Needs a first look from a maintainer" },
];

/**
 * RUL-1 to RUL-3. `checks` undefined leaves out the PR, linear history, and status check rules (template, org-github).
 * @param {string[] | undefined} checks
 */
function ruleset(checks) {
  /** @type {Record<string, unknown>[]} */
  const rules = [{ type: "deletion" }, { type: "non_fast_forward" }];
  if (checks) {
    rules.push(
      { type: "required_linear_history" },
      {
        type: "pull_request",
        parameters: {
          required_approving_review_count: 0,
          dismiss_stale_reviews_on_push: true,
          require_code_owner_review: false,
          require_last_push_approval: false,
          required_review_thread_resolution: true,
          allowed_merge_methods: ["squash"],
        },
      },
      {
        type: "required_status_checks",
        parameters: {
          strict_required_status_checks_policy: true,
          do_not_enforce_on_create: false,
          required_status_checks: checks.map((context) => ({
            context,
            integration_id: actionsAppId,
          })),
        },
      },
    );
  }
  return {
    name: "main",
    target: "branch",
    enforcement: "active",
    conditions: { ref_name: { include: ["~DEFAULT_BRANCH"], exclude: [] } },
    bypass_actors: [],
    rules,
  };
}

/**
 * @param {string} name
 * @param {string[]} checks
 * @returns {Profile}
 */
function sdk(name, checks) {
  return {
    name,
    repo: { ...baseRepo, has_issues: true, has_discussions: false, is_template: false },
    security,
    ruleset: ruleset(checks),
    labels,
  };
}

/** @type {Record<string, Profile>} */
export const profiles = {
  core: {
    name: "core",
    repo: { ...baseRepo, has_issues: true, has_discussions: true, is_template: false },
    security,
    ruleset: ruleset([
      "ci-gate",
      "analyze (csharp)",
      "analyze (javascript-typescript)",
      "dependency-review",
    ]),
    labels,
    // ISS-4. GitHub's API can't read a category's format, only whether it takes answers.
    discussionCategories: [
      { name: "Announcements", isAnswerable: false },
      { name: "Q&A", isAnswerable: true },
      { name: "Ideas", isAnswerable: false },
      { name: "Show and tell", isAnswerable: false },
    ],
  },
  "sdk-dotnet": sdk("sdk-dotnet", ["ci-gate", "analyze (csharp)", "dependency-review"]),
  "sdk-typescript": sdk("sdk-typescript", [
    "ci-gate",
    "analyze (javascript-typescript)",
    "dependency-review",
  ]),
  "sdk-dart": sdk("sdk-dart", ["ci-gate", "dependency-review"]),
  template: {
    name: "template",
    repo: { ...baseRepo, has_issues: false, has_discussions: false, is_template: true },
    security,
    ruleset: ruleset(undefined),
    labels: [],
    actions: { enabled: false },
  },
  "org-github": {
    name: "org-github",
    repo: { ...baseRepo, has_issues: false, has_discussions: false, is_template: false },
    security,
    ruleset: ruleset(undefined),
    labels: [],
    actions: { enabled: false },
  },
};

/** ORG-1: the fields the API can read. */
export const orgWant = {
  two_factor_requirement_enabled: true,
  sha_pinning_required: true,
  default_workflow_permissions: "read",
  can_approve_pull_request_reviews: false,
};

/** Fields the server adds to a ruleset, never compared (SET-4). */
const serverFields = new Set([
  "id",
  "source",
  "source_type",
  "node_id",
  "_links",
  "created_at",
  "updated_at",
  "current_user_can_bypass",
]);

/**
 * Compares only the fields `want` names. Arrays must match in length and element by element.
 * @param {unknown} want
 * @param {unknown} got
 * @param {string} path
 * @param {Difference[]} out
 */
export function subsetDiff(want, got, path, out) {
  if (Array.isArray(want)) {
    if (!Array.isArray(got) || got.length !== want.length) {
      out.push({ field: path, want, got });
      return;
    }
    want.forEach((item, i) => {
      subsetDiff(item, got[i], `${path}[${String(i)}]`, out);
    });
    return;
  }
  if (want !== null && typeof want === "object") {
    if (got === null || typeof got !== "object" || Array.isArray(got)) {
      out.push({ field: path, want, got });
      return;
    }
    for (const [key, value] of Object.entries(want)) {
      if (serverFields.has(key)) continue;
      subsetDiff(value, /** @type {Record<string, unknown>} */ (got)[key], `${path}.${key}`, out);
    }
    return;
  }
  if (want !== got) out.push({ field: path, want, got });
}

/**
 * Rules keyed by type, status checks sorted by name, so order never shows as drift.
 * @param {any} set
 */
function normalizeRuleset(set) {
  /** @type {Record<string, any>} */
  const rules = {};
  for (const rule of set?.rules ?? []) {
    const copy = structuredClone(rule);
    copy.parameters?.required_status_checks?.sort(
      (/** @type {{ context: string }} */ a, /** @type {{ context: string }} */ b) =>
        a.context.localeCompare(b.context),
    );
    copy.parameters?.allowed_merge_methods?.sort();
    rules[copy.type] = copy;
  }
  return { ...set, rules };
}

/** @param {Record<string, unknown>} set */
const withoutRules = (set) =>
  Object.fromEntries(Object.entries(set).filter(([key]) => key !== "rules"));

/**
 * @param {{ name: string }} a
 * @param {{ name: string }} b
 */
const byName = (a, b) => a.name.localeCompare(b.name);

/** @param {unknown} value */
const show = (value) => JSON.stringify(value);

/**
 * The ruleset part of the diff (RUL-1): exactly one ruleset, named main, matching the profile.
 * @param {Profile} profile
 * @param {any[]} rulesets the full rulesets on the repo (not inherited ones)
 * @returns {Difference[]}
 */
export function diffRulesets(profile, rulesets) {
  /** @type {Difference[]} */
  const out = [];
  const mainSet = rulesets.find((set) => set.name === "main");
  for (const other of rulesets.filter((set) => set !== mainSet)) {
    out.push({
      field: `ruleset "${String(other.name)}"`,
      want: "absent (RUL-1 allows only the main ruleset)",
      got: "present",
      manual: "delete it by hand, after checking why it was added",
    });
  }
  if (!mainSet) {
    out.push({ field: "ruleset.main", want: "present", got: "absent" });
    return out;
  }
  const want = normalizeRuleset(profile.ruleset);
  const got = normalizeRuleset(mainSet);
  subsetDiff(withoutRules(want), withoutRules(got), "ruleset", out);
  const wantTypes = Object.keys(want.rules);
  for (const type of wantTypes) {
    subsetDiff(want.rules[type], got.rules[type], `ruleset.rules.${type}`, out);
  }
  for (const type of Object.keys(got.rules).filter((t) => !wantTypes.includes(t))) {
    out.push({ field: `ruleset.rules.${type}`, want: "absent", got: "present" });
  }
  return out;
}

/**
 * @typedef {{
 *   repo: any,
 *   rulesets: any[] | "needs-public",
 *   labels: Label[],
 *   dependabotAlerts: boolean,
 *   dependabotSecurityUpdates: boolean,
 *   privateVulnerabilityReporting: boolean | "needs-public",
 *   actionsEnabled: boolean,
 *   codeScanningDefaultSetup: string | "needs-public",
 *   discussionCategories?: { name: string, isAnswerable: boolean }[],
 * }} Live
 */

/** Repo fields GitHub Free only honors on a public repo: a PATCH on a private repo is accepted but changes nothing. */
const needsPublicRepoFields = new Set(["allow_auto_merge"]);

/**
 * The whole diff for a repo. On a private repo, needsPublic fields (secret scanning, push protection, private
 * vulnerability reporting, the ruleset, auto merge) are reported as waiting for public instead of drift, whatever
 * their live value, and `--apply` leaves them alone (SET-2, SET-4).
 * @param {Profile} profile
 * @param {Live} live
 * @returns {Difference[]}
 */
export function diffRepo(profile, live) {
  /** @type {Difference[]} */
  const out = [];
  const isPrivate = live.repo.private === true;

  const repoFields = Object.fromEntries(
    Object.entries(profile.repo).filter(([key]) => !isPrivate || !needsPublicRepoFields.has(key)),
  );
  subsetDiff(repoFields, live.repo, "repo", out);

  const analysis = live.repo.security_and_analysis ?? {};
  /** @type {[string, boolean, unknown, boolean][]} [field, want, got, needsPublic] */
  const toggles = [
    [
      "security.secret_scanning",
      profile.security.secretScanning,
      analysis.secret_scanning?.status === "enabled",
      true,
    ],
    [
      "security.push_protection",
      profile.security.pushProtection,
      analysis.secret_scanning_push_protection?.status === "enabled",
      true,
    ],
    ["security.dependabot_alerts", profile.security.dependabotAlerts, live.dependabotAlerts, false],
    [
      "security.dependabot_security_updates",
      profile.security.dependabotSecurityUpdates,
      live.dependabotSecurityUpdates,
      false,
    ],
    [
      "security.private_vulnerability_reporting",
      profile.security.privateVulnerabilityReporting,
      live.privateVulnerabilityReporting,
      true,
    ],
  ];
  for (const [field, want, got, needsPublic] of toggles) {
    if (needsPublic && isPrivate) {
      out.push({ field, want, got, waiting: true });
    } else if (want !== got) {
      out.push({ field, want, got });
    }
  }

  if (isPrivate || live.rulesets === "needs-public") {
    out.push({ field: "ruleset.main", want: "present", got: "not checked", waiting: true });
  } else {
    out.push(...diffRulesets(profile, live.rulesets));
  }

  if (isPrivate) {
    for (const key of Object.keys(profile.repo).filter((k) => needsPublicRepoFields.has(k))) {
      out.push({
        field: `repo.${key}`,
        want: profile.repo[key],
        got: live.repo[key],
        waiting: true,
      });
    }
  }

  for (const label of profile.labels) {
    const found = live.labels.find((l) => l.name.toLowerCase() === label.name.toLowerCase());
    if (!found) {
      out.push({ field: `labels.${label.name}`, want: label, got: "absent" });
    } else if (
      found.color.toLowerCase() !== label.color ||
      (found.description ?? "") !== label.description
    ) {
      out.push({
        field: `labels.${label.name}`,
        want: label,
        got: { name: found.name, color: found.color, description: found.description },
      });
    }
  }

  if (profile.actions && profile.actions.enabled !== live.actionsEnabled) {
    out.push({ field: "actions.enabled", want: profile.actions.enabled, got: live.actionsEnabled });
  }

  // Spec 0008 CI-3: CodeQL runs from the reviewed workflow file, never default setup.
  if (live.codeScanningDefaultSetup !== "needs-public") {
    if (live.codeScanningDefaultSetup !== "not-configured") {
      out.push({
        field: "code_scanning.default_setup",
        want: "not-configured",
        got: live.codeScanningDefaultSetup,
      });
    }
  }

  if (profile.discussionCategories && live.discussionCategories) {
    const want = profile.discussionCategories.toSorted(byName);
    const got = live.discussionCategories
      .map((c) => ({ name: c.name, isAnswerable: c.isAnswerable }))
      .toSorted(byName);
    if (JSON.stringify(want) !== JSON.stringify(got)) {
      out.push({
        field: "discussions.categories",
        want: want.map((c) => c.name),
        got: got.map((c) => c.name),
        manual: "set the categories by hand in the repo's Discussions settings (ISS-4)",
      });
    }
  }
  return out;
}

/**
 * ORG-1. A field the API doesn't return becomes a manual tick in verify.md, not drift.
 * @param {Record<string, unknown>} live
 * @returns {Difference[]}
 */
export function diffOrg(live) {
  /** @type {Difference[]} */
  const out = [];
  for (const [field, want] of Object.entries(orgWant)) {
    const got = live[field];
    if (got === undefined) {
      out.push({
        field: `org.${field}`,
        want,
        got: "not readable",
        manual: "check it in the org settings and tick it in verify.md",
      });
    } else if (got !== want) {
      out.push({
        field: `org.${field}`,
        want,
        got,
        manual: "change it in the org settings (ORG-1)",
      });
    }
  }
  return out;
}

/** Thrown for usage and gh errors (exit 2). */
export class SettingsError extends Error {}

/**
 * @param {ApiResponse} response
 * @param {string} what
 */
function expectOk(response, what) {
  if (response.status < 200 || response.status >= 300) {
    throw new SettingsError(
      `${what} failed: HTTP ${String(response.status)} ${JSON.stringify(response.data)}`,
    );
  }
  return response.data;
}

/**
 * An unexpected response shape is an error (exit 2), never drift (exit 1).
 * @param {unknown} data
 * @param {string} what
 * @returns {any[]}
 */
function expectArray(data, what) {
  if (!Array.isArray(data)) {
    throw new SettingsError(`${what} returned ${JSON.stringify(data)}, not a list.`);
  }
  return data;
}

/**
 * Reads everything a profile compares.
 * @param {Api} api
 * @param {string} repo
 * @param {Profile} profile
 * @returns {Live}
 */
export function readLive(api, repo, profile) {
  const repoData = expectOk(api("GET", `repos/${repo}`), `reading ${repo}`);
  const isPrivate = repoData.private === true;

  /** @type {any[] | "needs-public"} */
  let rulesets;
  const list = api("GET", `repos/${repo}/rulesets?includes_parents=false`);
  if (list.status === 403 && isPrivate) {
    rulesets = "needs-public";
  } else {
    rulesets = expectArray(expectOk(list, "listing rulesets"), "listing rulesets").map((set) =>
      expectOk(
        api("GET", `repos/${repo}/rulesets/${String(set.id)}`),
        `reading ruleset ${set.name}`,
      ),
    );
  }

  const labelData = /** @type {Label[]} */ (
    expectArray(
      expectOk(api("GET", `repos/${repo}/labels?per_page=100`), "listing labels"),
      "listing labels",
    )
  );

  const alerts = api("GET", `repos/${repo}/vulnerability-alerts`);
  if (alerts.status !== 204 && alerts.status !== 404) expectOk(alerts, "reading Dependabot alerts");

  const fixes = expectOk(
    api("GET", `repos/${repo}/automated-security-fixes`),
    "reading Dependabot security updates",
  );

  const reporting = api("GET", `repos/${repo}/private-vulnerability-reporting`);
  /** @type {boolean | "needs-public"} */
  const privateVulnerabilityReporting =
    reporting.status >= 400 && isPrivate
      ? "needs-public"
      : expectOk(reporting, "reading private vulnerability reporting").enabled === true;

  const actions = expectOk(
    api("GET", `repos/${repo}/actions/permissions`),
    "reading Actions permissions",
  );

  const setup = api("GET", `repos/${repo}/code-scanning/default-setup`);
  /** @type {string} */
  const codeScanningDefaultSetup =
    setup.status >= 400 && isPrivate
      ? "needs-public"
      : String(expectOk(setup, "reading CodeQL default setup").state);

  /** @type {Live} */
  const live = {
    repo: repoData,
    rulesets,
    labels: labelData,
    dependabotAlerts: alerts.status === 204,
    dependabotSecurityUpdates: fixes.enabled === true,
    privateVulnerabilityReporting,
    actionsEnabled: actions.enabled === true,
    codeScanningDefaultSetup,
  };

  if (profile.discussionCategories && repoData.has_discussions === true) {
    const [owner, name] = repo.split("/");
    const result = expectOk(
      api("POST", "graphql", {
        query:
          "query($owner: String!, $name: String!) { repository(owner: $owner, name: $name) { discussionCategories(first: 25) { nodes { name isAnswerable } } } }",
        variables: { owner, name },
      }),
      "reading Discussions categories",
    );
    // GraphQL answers HTTP 200 even when the query fails, with the reason in `errors`.
    const nodes = result?.data?.repository?.discussionCategories?.nodes;
    if (result?.errors || !Array.isArray(nodes)) {
      throw new SettingsError(
        `Reading Discussions categories failed: ${JSON.stringify(result?.errors ?? result)}`,
      );
    }
    live.discussionCategories = nodes;
  }
  return live;
}

/**
 * @param {Api} api
 * @param {string} org
 */
export function readOrg(api, org) {
  const read = (/** @type {string} */ path, /** @type {string} */ what) => {
    const response = api("GET", path);
    if (response.status === 403) {
      throw new SettingsError(
        `${what} needs an org owner's login with the admin:org scope. Run \`gh auth refresh -s admin:org\`, then try again.`,
      );
    }
    return expectOk(response, what);
  };
  const orgData = read(`orgs/${org}`, `Reading org ${org}`);
  const actions = read(`orgs/${org}/actions/permissions`, "Reading org Actions permissions");
  const workflow = read(
    `orgs/${org}/actions/permissions/workflow`,
    "Reading org workflow permissions",
  );
  return {
    two_factor_requirement_enabled: orgData.two_factor_requirement_enabled,
    sha_pinning_required: actions.sha_pinning_required,
    default_workflow_permissions: workflow.default_workflow_permissions,
    can_approve_pull_request_reviews: workflow.can_approve_pull_request_reviews,
  };
}

/**
 * Writes the differences. Skips fields waiting for public and ones only a person can fix.
 * @param {Api} api
 * @param {string} repo
 * @param {Profile} profile
 * @param {Live} live
 * @param {Difference[]} diff
 */
export function applyDiff(api, repo, profile, live, diff) {
  const todo = diff.filter((d) => !d.waiting && !d.manual);
  const has = (/** @type {string} */ prefix) => todo.some((d) => d.field.startsWith(prefix));

  /** @type {Record<string, unknown>} */
  const patch = {};
  for (const d of todo.filter((x) => x.field.startsWith("repo."))) {
    const key = d.field.slice("repo.".length);
    patch[key] = profile.repo[key];
  }
  /** @type {Record<string, { status: string }>} */
  const analysis = {};
  if (has("security.secret_scanning")) {
    analysis.secret_scanning = { status: profile.security.secretScanning ? "enabled" : "disabled" };
  }
  if (has("security.push_protection")) {
    analysis.secret_scanning_push_protection = {
      status: profile.security.pushProtection ? "enabled" : "disabled",
    };
  }
  if (Object.keys(analysis).length > 0) patch.security_and_analysis = analysis;
  if (Object.keys(patch).length > 0) {
    expectOk(api("PATCH", `repos/${repo}`, patch), "updating repo settings");
  }

  const toggle = (
    /** @type {string} */ path,
    /** @type {boolean} */ on,
    /** @type {string} */ what,
  ) => expectOk(api(on ? "PUT" : "DELETE", `repos/${repo}/${path}`), what);
  if (has("security.dependabot_alerts")) {
    toggle("vulnerability-alerts", profile.security.dependabotAlerts, "setting Dependabot alerts");
  }
  if (has("security.dependabot_security_updates")) {
    toggle(
      "automated-security-fixes",
      profile.security.dependabotSecurityUpdates,
      "setting Dependabot security updates",
    );
  }
  if (has("security.private_vulnerability_reporting")) {
    toggle(
      "private-vulnerability-reporting",
      profile.security.privateVulnerabilityReporting,
      "setting private vulnerability reporting",
    );
  }

  if (has("ruleset") && Array.isArray(live.rulesets)) {
    const mainSet = live.rulesets.find((set) => set.name === "main");
    if (mainSet) {
      expectOk(
        api("PUT", `repos/${repo}/rulesets/${String(mainSet.id)}`, profile.ruleset),
        "updating the main ruleset",
      );
    } else {
      expectOk(api("POST", `repos/${repo}/rulesets`, profile.ruleset), "creating the main ruleset");
    }
  }

  for (const d of todo.filter((x) => x.field.startsWith("labels."))) {
    const label = profile.labels.find((l) => `labels.${l.name}` === d.field);
    if (!label) continue;
    if (d.got === "absent") {
      expectOk(api("POST", `repos/${repo}/labels`, label), `creating label ${label.name}`);
    } else {
      expectOk(
        api("PATCH", `repos/${repo}/labels/${encodeURIComponent(label.name)}`, {
          color: label.color,
          description: label.description,
        }),
        `updating label ${label.name}`,
      );
    }
  }

  if (has("actions.enabled") && profile.actions) {
    expectOk(
      api("PUT", `repos/${repo}/actions/permissions`, { enabled: profile.actions.enabled }),
      "setting Actions",
    );
  }

  if (has("code_scanning.default_setup")) {
    expectOk(
      api("PATCH", `repos/${repo}/code-scanning/default-setup`, { state: "not-configured" }),
      "turning off CodeQL default setup",
    );
  }
}

/**
 * @param {Difference[]} diff
 * @param {(line: string) => void} print
 * @returns {boolean} true when something is out of step
 */
export function report(diff, print) {
  let drift = false;
  for (const d of diff) {
    if (d.waiting) {
      print(`waiting for public  ${d.field}: want ${show(d.want)} once the repo is public`);
    } else if (d.manual && d.got === "not readable") {
      print(`manual  ${d.field}: the API doesn't return it; ${d.manual}`);
    } else {
      drift = true;
      print(
        `drift  ${d.field}: want ${show(d.want)}, got ${show(d.got)}${d.manual ? ` (${d.manual})` : ""}`,
      );
    }
  }
  return drift;
}

/**
 * Calls `gh api` with the caller's own login.
 * @type {Api}
 */
export function ghApi(method, path, body) {
  const args = [
    "api",
    "--method",
    method,
    "--include",
    "-H",
    "Accept: application/vnd.github+json",
    "-H",
    "X-GitHub-Api-Version: 2022-11-28",
    path,
  ];
  if (body !== undefined) args.push("--input", "-");
  const result = spawnSync("gh", args, {
    encoding: "utf8",
    input: body === undefined ? undefined : JSON.stringify(body),
    shell: false,
  });
  if (result.error) {
    throw new SettingsError(
      "The gh CLI isn't installed or isn't on PATH. Install it from https://cli.github.com, then run `gh auth login`.",
    );
  }
  const out = result.stdout.replaceAll("\r\n", "\n");
  const statusLine = /^HTTP\/[\d.]+ (\d{3})/.exec(out);
  if (!statusLine) {
    throw new SettingsError(
      `gh api ${path} gave no HTTP response. Are you logged in? Run \`gh auth status\`, then \`gh auth login\` if needed.\n${result.stderr.trim()}`,
    );
  }
  const split = out.indexOf("\n\n");
  const text = split >= 0 ? out.slice(split + 2).trim() : "";
  let data = null;
  if (text) {
    try {
      data = JSON.parse(text);
    } catch {
      data = text;
    }
  }
  return { status: Number(statusLine[1]), data };
}

/**
 * @param {string[]} argv
 * @returns {{ repo?: string, profile?: string, apply: boolean, org?: string }}
 */
export function parseArgs(argv) {
  /** @type {{ repo?: string, profile?: string, apply: boolean, org?: string }} */
  const args = { apply: false };
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    if (arg === "--apply") args.apply = true;
    else if (arg === "--profile") args.profile = argv[++i];
    else if (arg === "--org") args.org = argv[++i];
    else if (arg?.startsWith("--")) throw new SettingsError(`Unknown option ${arg}.`);
    else if (arg) args.repo = arg;
  }
  return args;
}

const usage = `Usage:
  node tooling/scripts/github-settings.mjs <owner/repo> --profile <${Object.keys(profiles).join("|")}> [--apply]
  node tooling/scripts/github-settings.mjs --org <org>`;

/**
 * @param {string[]} argv
 * @param {Api} api
 * @param {(line: string) => void} print
 * @returns {number} the exit code
 */
export function main(argv, api, print) {
  try {
    const args = parseArgs(argv);
    if (args.org) {
      if (args.apply) throw new SettingsError("--org only checks; set ORG-1 in the org settings.");
      const drift = report(diffOrg(readOrg(api, args.org)), print);
      print(drift ? `${args.org}: out of step with ORG-1.` : `${args.org}: matches ORG-1.`);
      return drift ? 1 : 0;
    }
    if (!args.repo || !/^[\w.-]+\/[\w.-]+$/.test(args.repo) || !args.profile) {
      throw new SettingsError(usage);
    }
    const profile = profiles[args.profile];
    if (!profile) throw new SettingsError(`Unknown profile "${args.profile}".\n${usage}`);

    let live = readLive(api, args.repo, profile);
    let diff = diffRepo(profile, live);
    if (args.apply) {
      applyDiff(api, args.repo, profile, live, diff);
      live = readLive(api, args.repo, profile);
      diff = diffRepo(profile, live);
    }
    const drift = report(diff, print);
    print(
      drift
        ? `${args.repo}: out of step with the ${profile.name} profile.`
        : `${args.repo}: matches the ${profile.name} profile.`,
    );
    return drift ? 1 : 0;
  } catch (error) {
    if (error instanceof SettingsError) {
      print(error.message);
      return 2;
    }
    throw error;
  }
}

if (process.argv[1] && resolve(process.argv[1]) === import.meta.filename) {
  process.exitCode = main(process.argv.slice(2), ghApi, (line) => {
    console.log(line);
  });
}
