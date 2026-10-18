// Spec 0010 SET-4, RUL-1 to RUL-3, ORG-1, AC-8: github-settings.mjs against a fake GitHub API (no network).
// The fake keeps state, so --apply is proven by the check that follows it.
import assert from "node:assert/strict";
import { chmodSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { after, describe, test } from "node:test";
import {
  actionsAppId,
  diffOrg,
  ghApi,
  main,
  parseArgs,
  profiles,
  readLive,
  SettingsError,
  subsetDiff,
} from "./github-settings.mjs";

const repo = "orvanohq/orvano";

/** @param {string} name */
function profile(name) {
  const found = profiles[name];
  assert.ok(found, `profile ${name}`);
  return found;
}

/**
 * A live repo that matches a profile exactly, as GitHub would return it (with server fields added). Loosely typed,
 * since tests change it freely.
 * @param {string} name
 * @returns {any}
 */
function matchingState(name) {
  const p = profile(name);
  return {
    repo: {
      ...p.repo,
      private: false,
      id: 1,
      full_name: repo,
      security_and_analysis: {
        secret_scanning: { status: "enabled" },
        secret_scanning_push_protection: { status: "enabled" },
        dependabot_security_updates: { status: "enabled" },
      },
    },
    /** @type {any[]} */
    rulesets: [
      {
        ...structuredClone(p.ruleset),
        id: 42,
        source: repo,
        source_type: "Repository",
        node_id: "RRS_1",
        _links: { self: { href: "x" } },
        created_at: "2026-10-16T00:00:00Z",
        updated_at: "2026-10-16T00:00:00Z",
        current_user_can_bypass: "never",
      },
    ],
    labels: [
      ...p.labels.map((l) => ({ ...l, id: 1, default: false })),
      { name: "documentation", color: "0075ca", description: "Docs", id: 2, default: true },
    ],
    dependabotAlerts: true,
    dependabotSecurityUpdates: true,
    privateVulnerabilityReporting: true,
    actionsEnabled: p.actions?.enabled ?? true,
    defaultSetup: "not-configured",
    categories:
      p.discussionCategories?.map((c) => Object.assign({}, c, { slug: c.name.toLowerCase() })) ??
      [],
    org: {
      two_factor_requirement_enabled: true,
      sha_pinning_required: true,
      default_workflow_permissions: "read",
      can_approve_pull_request_reviews: false,
    },
  };
}

/** @param {unknown} data */
const ok = (data) => ({ status: 200, data });

/**
 * A fake `gh api` over a state object. Records every call.
 * @param {ReturnType<typeof matchingState>} state
 */
function fakeApi(state) {
  /** @type {{ method: string, path: string, body?: any }[]} */
  const calls = [];
  /** @type {import("./github-settings.mjs").Api} */
  const api = (method, path, rawBody) => {
    const body = /** @type {any} */ (rawBody);
    calls.push({ method, path, body });
    const r = `repos/${repo}`;
    const isPrivate = state.repo.private;
    switch (`${method} ${path}`) {
      case `GET ${r}`:
        return ok(state.repo);
      case `PATCH ${r}`: {
        const { security_and_analysis: analysis, ...rest } = body;
        Object.assign(state.repo, rest);
        if (analysis) Object.assign(state.repo.security_and_analysis, analysis);
        return ok(state.repo);
      }
      case `GET ${r}/rulesets?includes_parents=false`:
        if (isPrivate) return { status: 403, data: { message: "Upgrade to GitHub Pro" } };
        return ok(state.rulesets.map((/** @type {any} */ s) => ({ id: s.id, name: s.name })));
      case `POST ${r}/rulesets`:
        state.rulesets.push({ ...body, id: 99 });
        return ok(body);
      case `GET ${r}/labels?per_page=100`:
        return ok(state.labels);
      case `POST ${r}/labels`:
        state.labels.push(body);
        return { status: 201, data: body };
      case `GET ${r}/vulnerability-alerts`:
        return { status: state.dependabotAlerts ? 204 : 404, data: null };
      case `PUT ${r}/vulnerability-alerts`:
        state.dependabotAlerts = true;
        return { status: 204, data: null };
      case `GET ${r}/automated-security-fixes`:
        return ok({ enabled: state.dependabotSecurityUpdates, paused: false });
      case `PUT ${r}/automated-security-fixes`:
        state.dependabotSecurityUpdates = true;
        return { status: 204, data: null };
      case `GET ${r}/private-vulnerability-reporting`:
        return ok({ enabled: state.privateVulnerabilityReporting });
      case `PUT ${r}/private-vulnerability-reporting`:
        state.privateVulnerabilityReporting = true;
        return { status: 204, data: null };
      case `GET ${r}/actions/permissions`:
        return ok({ enabled: state.actionsEnabled, allowed_actions: "all" });
      case `PUT ${r}/actions/permissions`:
        state.actionsEnabled = body.enabled;
        return { status: 204, data: null };
      case `GET ${r}/code-scanning/default-setup`:
        if (isPrivate)
          return { status: 403, data: { message: "Advanced Security must be enabled" } };
        return ok({ state: state.defaultSetup, languages: [] });
      case `PATCH ${r}/code-scanning/default-setup`:
        state.defaultSetup = body.state;
        return ok({});
      case "POST graphql":
        return ok({
          data: { repository: { discussionCategories: { nodes: state.categories } } },
        });
      case "GET orgs/orvanohq":
        return ok({
          login: "orvanohq",
          two_factor_requirement_enabled: state.org.two_factor_requirement_enabled,
        });
      case "GET orgs/orvanohq/actions/permissions":
        return ok({
          enabled_repositories: "all",
          sha_pinning_required: state.org.sha_pinning_required,
        });
      case "GET orgs/orvanohq/actions/permissions/workflow":
        return ok({
          default_workflow_permissions: state.org.default_workflow_permissions,
          can_approve_pull_request_reviews: state.org.can_approve_pull_request_reviews,
        });
      default: {
        const ruleset = /^(GET|PUT) repos\/[^/]+\/[^/]+\/rulesets\/(\d+)$/.exec(
          `${method} ${path}`,
        );
        if (ruleset) {
          const index = state.rulesets.findIndex(
            (/** @type {any} */ s) => String(s.id) === ruleset[2],
          );
          if (index < 0) return { status: 404, data: { message: "Not Found" } };
          if (method === "PUT") state.rulesets[index] = { ...body, id: state.rulesets[index].id };
          return ok(state.rulesets[index]);
        }
        const label = /^PATCH repos\/[^/]+\/[^/]+\/labels\/(.+)$/.exec(`${method} ${path}`);
        if (label) {
          const found = state.labels.find(
            (/** @type {any} */ l) => l.name === decodeURIComponent(label[1] ?? ""),
          );
          if (found) Object.assign(found, body);
          return ok(found);
        }
        return { status: 404, data: { message: `fake has no route for ${method} ${path}` } };
      }
    }
  };
  return { api, calls };
}

/**
 * @param {string[]} argv
 * @param {ReturnType<typeof matchingState>} state
 */
function run(argv, state) {
  const { api, calls } = fakeApi(state);
  /** @type {string[]} */
  const lines = [];
  const code = main(argv, api, (line) => lines.push(line));
  return { code, out: lines.join("\n"), calls };
}

const writes = (/** @type {{ method: string }[]} */ calls) =>
  calls.filter(
    (c) => c.method !== "GET" && !(c.method === "POST" && "path" in c && c.path === "graphql"),
  );

describe("profiles", () => {
  test("core requires the four RUL-3 checks, each pinned to the GitHub Actions app", () => {
    const rules = /** @type {any[]} */ (profile("core").ruleset.rules);
    const checks = rules.find((r) => r.type === "required_status_checks").parameters;
    assert.deepEqual(
      checks.required_status_checks.map((/** @type {{ context: string }} */ c) => c.context),
      ["ci-gate", "analyze (csharp)", "analyze (javascript-typescript)", "dependency-review"],
    );
    assert.ok(
      checks.required_status_checks.every(
        (/** @type {any} */ c) => c.integration_id === actionsAppId,
      ),
    );
    assert.equal(checks.strict_required_status_checks_policy, true);
  });

  test("every SDK profile requires ci-gate and dependency-review, Dart has no CodeQL", () => {
    /** @type {Record<string, string[]>} */
    const want = {
      "sdk-dotnet": ["ci-gate", "analyze (csharp)", "dependency-review"],
      "sdk-typescript": ["ci-gate", "analyze (javascript-typescript)", "dependency-review"],
      "sdk-dart": ["ci-gate", "dependency-review"],
    };
    for (const [name, checks] of Object.entries(want)) {
      const rules = /** @type {any[]} */ (profile(name).ruleset.rules);
      const got = rules
        .find((r) => r.type === "required_status_checks")
        .parameters.required_status_checks.map((/** @type {{ context: string }} */ c) => c.context);
      assert.deepEqual(got, checks, name);
    }
  });

  test("template and org-github keep only deletion and non_fast_forward, with Actions off", () => {
    for (const name of ["template", "org-github"]) {
      const p = profile(name);
      const types = /** @type {any[]} */ (p.ruleset.rules).map((r) => r.type);
      assert.deepEqual(types, ["deletion", "non_fast_forward"], name);
      assert.deepEqual(p.actions, { enabled: false });
      assert.equal(p.repo.has_issues, false);
    }
    assert.equal(profile("template").repo.is_template, true);
  });

  test("every profile: no bypass, squash only, sign off on web commits (RUL-1, SET-1)", () => {
    for (const p of Object.values(profiles)) {
      assert.deepEqual(p.ruleset.bypass_actors, [], p.name);
      assert.equal(p.ruleset.enforcement, "active");
      assert.equal(p.repo.allow_merge_commit, false);
      assert.equal(p.repo.allow_rebase_merge, false);
      assert.equal(p.repo.squash_merge_commit_message, "COMMIT_MESSAGES");
      assert.equal(p.repo.web_commit_signoff_required, true);
      const pr = /** @type {any[]} */ (p.ruleset.rules).find((r) => r.type === "pull_request");
      if (pr) {
        assert.deepEqual(pr.parameters.allowed_merge_methods, ["squash"]);
        assert.equal(pr.parameters.required_approving_review_count, 0);
      }
    }
  });

  test("only core has Discussions, with exactly the ISS-4 categories", () => {
    assert.deepEqual(
      profile("core").discussionCategories?.map((c) => c.name),
      ["Announcements", "Q&A", "Ideas", "Show and tell"],
    );
    for (const name of ["sdk-dotnet", "sdk-typescript", "sdk-dart"]) {
      assert.equal(profile(name).repo.has_discussions, false);
    }
  });
});

describe("subsetDiff", () => {
  test("ignores fields the profile doesn't name and server fields", () => {
    /** @type {any[]} */
    const out = [];
    subsetDiff(
      { a: 1, b: { c: [1, 2] } },
      { a: 1, extra: true, id: 7, b: { c: [1, 2], d: 3 } },
      "x",
      out,
    );
    assert.deepEqual(out, []);
  });

  test("reports a changed value, a longer array, and a missing object with their paths", () => {
    /** @type {any[]} */
    const out = [];
    subsetDiff(
      { a: 1, list: [], o: { k: true } },
      { a: 2, list: [{ actor_id: 5 }], o: null },
      "x",
      out,
    );
    assert.deepEqual(
      out.map((d) => d.field),
      ["x.a", "x.list", "x.o"],
    );
  });
});

describe("check mode", () => {
  for (const name of Object.keys(profiles)) {
    test(`a repo matching the ${name} profile exits 0 and writes nothing`, () => {
      const result = run([repo, "--profile", name], matchingState(name));
      assert.equal(result.code, 0, result.out);
      assert.match(result.out, new RegExp(`matches the ${name} profile`));
      assert.deepEqual(writes(result.calls), []);
    });
  }

  test("status checks and rules in another order are not drift", () => {
    const state = matchingState("core");
    const set = state.rulesets[0];
    set.rules.reverse();
    set.rules
      .find((/** @type {any} */ r) => r.type === "required_status_checks")
      .parameters.required_status_checks.reverse();
    const result = run([repo, "--profile", "core"], state);
    assert.equal(result.code, 0, result.out);
  });

  test("merge commits turned on by hand is drift, named by field (AC-8)", () => {
    const state = matchingState("core");
    state.repo.allow_merge_commit = true;
    const result = run([repo, "--profile", "core"], state);
    assert.equal(result.code, 1);
    assert.match(result.out, /drift {2}repo\.allow_merge_commit: want false, got true/);
  });

  for (const [what, change, field] of /** @type {[string, (s: any) => void, RegExp][]} */ ([
    [
      "a bypass actor",
      (s) => s.rulesets[0].bypass_actors.push({ actor_id: 5, actor_type: "OrganizationAdmin" }),
      /ruleset\.bypass_actors/,
    ],
    [
      "a dropped required check",
      (s) =>
        s.rulesets[0].rules
          .find((/** @type {any} */ r) => r.type === "required_status_checks")
          .parameters.required_status_checks.pop(),
      /required_status_checks/,
    ],
    [
      "a check from another app",
      (s) => {
        s.rulesets[0].rules.find(
          (/** @type {any} */ r) => r.type === "required_status_checks",
        ).parameters.required_status_checks[0].integration_id = 1;
      },
      /integration_id/,
    ],
    [
      "an extra rule",
      (s) => s.rulesets[0].rules.push({ type: "creation" }),
      /ruleset\.rules\.creation: want "absent"/,
    ],
    [
      "enforcement off",
      (s) => {
        s.rulesets[0].enforcement = "disabled";
      },
      /ruleset\.enforcement/,
    ],
    [
      "merge allowed by the PR rule",
      (s) => {
        s.rulesets[0].rules
          .find((/** @type {any} */ r) => r.type === "pull_request")
          .parameters.allowed_merge_methods.push("merge");
      },
      /allowed_merge_methods/,
    ],
    [
      "the main ruleset missing",
      (s) => {
        s.rulesets = [];
      },
      /ruleset\.main: want "present", got "absent"/,
    ],
    [
      "a second ruleset",
      (s) => s.rulesets.push({ id: 7, name: "temp", rules: [] }),
      /ruleset "temp".*delete it by hand/,
    ],
    [
      "secret scanning off",
      (s) => {
        s.repo.security_and_analysis.secret_scanning.status = "disabled";
      },
      /security\.secret_scanning/,
    ],
    [
      "Dependabot alerts off",
      (s) => {
        s.dependabotAlerts = false;
      },
      /security\.dependabot_alerts/,
    ],
    [
      "Dependabot security updates off",
      (s) => {
        s.dependabotSecurityUpdates = false;
      },
      /security\.dependabot_security_updates/,
    ],
    [
      "private reporting off",
      (s) => {
        s.privateVulnerabilityReporting = false;
      },
      /security\.private_vulnerability_reporting/,
    ],
    [
      "a missing label",
      (s) => {
        s.labels = s.labels.filter((/** @type {any} */ l) => l.name !== "triage");
      },
      /labels\.triage/,
    ],
    [
      "a recolored label",
      (s) => {
        s.labels[0].color = "000000";
      },
      /labels\.bug/,
    ],
    [
      "CodeQL default setup on",
      (s) => {
        s.defaultSetup = "configured";
      },
      /code_scanning\.default_setup/,
    ],
    [
      "the General category",
      (s) => s.categories.push({ name: "General", isAnswerable: false }),
      /discussions\.categories.*by hand/,
    ],
  ])) {
    test(`${what} is drift`, () => {
      const state = matchingState("core");
      change(state);
      const result = run([repo, "--profile", "core"], state);
      assert.equal(result.code, 1, result.out);
      assert.match(result.out, field);
    });
  }

  test("extra labels are left alone", () => {
    const state = matchingState("core");
    state.labels.push({
      name: "good first issue",
      color: "7057ff",
      description: "",
      id: 9,
      default: true,
    });
    assert.equal(run([repo, "--profile", "core"], state).code, 0);
  });

  test("Actions on in a template repo is drift", () => {
    const state = matchingState("template");
    state.actionsEnabled = true;
    const result = run([repo, "--profile", "template"], state);
    assert.equal(result.code, 1);
    assert.match(result.out, /actions\.enabled: want false, got true/);
  });

  test("on a private repo the five needsPublic fields wait for public instead of drifting (OPN-1 item 6)", () => {
    const state = matchingState("core");
    state.repo.private = true;
    delete (/** @type {any} */ (state.repo).security_and_analysis);
    state.rulesets = [];
    state.privateVulnerabilityReporting = false;
    const result = run([repo, "--profile", "core"], state);
    assert.equal(result.code, 0, result.out);
    const waiting = result.out.split("\n").filter((line) => line.startsWith("waiting for public"));
    assert.deepEqual(
      waiting.map((line) => /waiting for public {2}([^:]+):/.exec(line)?.[1]),
      [
        "security.secret_scanning",
        "security.push_protection",
        "security.private_vulnerability_reporting",
        "ruleset.main",
        "repo.allow_auto_merge",
      ],
    );
  });

  test("a private repo with auto merge off exits 0, since GitHub Free can't turn it on there (step 11a)", () => {
    const state = matchingState("core");
    state.repo.private = true;
    state.repo.allow_auto_merge = false;
    const result = run([repo, "--profile", "core"], state);
    assert.equal(result.code, 0, result.out);
    assert.match(
      result.out,
      /waiting for public {2}repo\.allow_auto_merge: want true once the repo is public/,
    );
    assert.doesNotMatch(result.out, /drift {2}repo\.allow_auto_merge/);
  });

  test("a public repo with auto merge off is drift (step 11a)", () => {
    const state = matchingState("core");
    state.repo.allow_auto_merge = false;
    const result = run([repo, "--profile", "core"], state);
    assert.equal(result.code, 1);
    assert.match(result.out, /drift {2}repo\.allow_auto_merge: want true, got false/);
  });
});

describe("--apply", () => {
  test("on a private repo, the PATCH leaves out allow_auto_merge (step 11a)", () => {
    const state = matchingState("core");
    state.repo.private = true;
    state.repo.allow_auto_merge = false;
    state.repo.has_wiki = true;
    const result = run([repo, "--profile", "core", "--apply"], state);
    assert.equal(result.code, 0, result.out);
    const patch = result.calls.find((c) => c.method === "PATCH" && c.path === `repos/${repo}`);
    assert.deepEqual(patch?.body, { has_wiki: false });
  });

  test("on a public repo, --apply turns auto merge on (step 11a)", () => {
    const state = matchingState("core");
    state.repo.allow_auto_merge = false;
    const result = run([repo, "--profile", "core", "--apply"], state);
    assert.equal(result.code, 0, result.out);
    const patch = result.calls.find((c) => c.method === "PATCH" && c.path === `repos/${repo}`);
    assert.deepEqual(patch?.body, { allow_auto_merge: true });
    assert.equal(state.repo.allow_auto_merge, true);
  });

  test("writes every difference, then checks again and exits 0", () => {
    const state = matchingState("core");
    state.repo.allow_merge_commit = true;
    state.repo.has_wiki = true;
    state.repo.security_and_analysis.secret_scanning_push_protection.status = "disabled";
    state.dependabotAlerts = false;
    state.dependabotSecurityUpdates = false;
    state.privateVulnerabilityReporting = false;
    state.rulesets[0].bypass_actors = [{ actor_id: 5, actor_type: "OrganizationAdmin" }];
    state.labels = state.labels.filter((/** @type {any} */ l) => l.name !== "triage");
    state.labels[0] = { ...state.labels[0], color: "ffffff" };
    state.defaultSetup = "configured";
    const result = run([repo, "--profile", "core", "--apply"], state);
    assert.equal(result.code, 0, result.out);
    const patch = result.calls.find((c) => c.method === "PATCH" && c.path === `repos/${repo}`);
    assert.deepEqual(patch?.body, {
      allow_merge_commit: false,
      has_wiki: false,
      security_and_analysis: { secret_scanning_push_protection: { status: "enabled" } },
    });
    assert.ok(
      result.calls.some((c) => c.method === "PUT" && c.path === `repos/${repo}/rulesets/42`),
    );
    assert.deepEqual(state.rulesets[0].bypass_actors, []);
  });

  test("creates the main ruleset when there is none", () => {
    const state = matchingState("sdk-dart");
    state.rulesets = [];
    const result = run([repo, "--profile", "sdk-dart", "--apply"], state);
    assert.equal(result.code, 0, result.out);
    assert.ok(result.calls.some((c) => c.method === "POST" && c.path === `repos/${repo}/rulesets`));
  });

  test("turns Actions off for a template", () => {
    const state = matchingState("template");
    state.actionsEnabled = true;
    const result = run([repo, "--profile", "template", "--apply"], state);
    assert.equal(result.code, 0, result.out);
    assert.equal(state.actionsEnabled, false);
  });

  test("never deletes a second ruleset or edits Discussions categories, so it still exits 1", () => {
    const state = matchingState("core");
    state.rulesets.push({ id: 7, name: "temp", rules: [] });
    state.categories.push({ name: "Polls", isAnswerable: false, slug: "polls" });
    const result = run([repo, "--profile", "core", "--apply"], state);
    assert.equal(result.code, 1);
    assert.deepEqual(writes(result.calls), []);
    assert.equal(state.rulesets.length, 2);
  });

  test("skips needsPublic fields on a private repo", () => {
    const state = matchingState("core");
    state.repo.private = true;
    state.rulesets = [];
    state.privateVulnerabilityReporting = false;
    const result = run([repo, "--profile", "core", "--apply"], state);
    assert.equal(result.code, 0, result.out);
    assert.deepEqual(writes(result.calls), []);
  });
});

describe("--org", () => {
  test("an org matching ORG-1 exits 0", () => {
    const result = run(["--org", "orvanohq"], matchingState("core"));
    assert.equal(result.code, 0, result.out);
  });

  test("a writable workflow token is drift", () => {
    const state = matchingState("core");
    state.org.default_workflow_permissions = "write";
    const result = run(["--org", "orvanohq"], state);
    assert.equal(result.code, 1);
    assert.match(result.out, /org\.default_workflow_permissions: want "read", got "write"/);
  });

  test("a field the API doesn't return becomes a manual tick, not drift", () => {
    const diff = diffOrg({
      two_factor_requirement_enabled: true,
      default_workflow_permissions: "read",
      can_approve_pull_request_reviews: false,
    });
    assert.equal(diff.length, 1);
    assert.match(diff[0]?.manual ?? "", /verify\.md/);
    const state = matchingState("core");
    /** @type {any} */ (state.org).sha_pinning_required = undefined;
    assert.equal(run(["--org", "orvanohq"], state).code, 0);
  });

  test("a token without admin:org exits 2 with the fix", () => {
    const state = matchingState("core");
    const { api } = fakeApi(state);
    /** @type {string[]} */
    const lines = [];
    const code = main(
      ["--org", "orvanohq"],
      (method, path) =>
        path === "orgs/orvanohq/actions/permissions"
          ? { status: 403, data: {} }
          : api(method, path),
      (line) => lines.push(line),
    );
    assert.equal(code, 2);
    assert.match(lines.join("\n"), /gh auth refresh -s admin:org/);
  });

  test("--org with --apply is refused", () => {
    assert.equal(run(["--org", "orvanohq", "--apply"], matchingState("core")).code, 2);
  });
});

describe("arguments", () => {
  test("an unknown profile exits 2", () => {
    const result = run([repo, "--profile", "nope"], matchingState("core"));
    assert.equal(result.code, 2);
    assert.match(result.out, /Unknown profile "nope"/);
  });

  test("a missing profile or repo prints usage and exits 2", () => {
    assert.equal(run([repo], matchingState("core")).code, 2);
    assert.equal(run(["--profile", "core"], matchingState("core")).code, 2);
  });

  test("an unknown option exits 2", () => {
    assert.equal(run([repo, "--profile", "core", "--force"], matchingState("core")).code, 2);
  });

  test("parses the flags", () => {
    assert.deepEqual(parseArgs([repo, "--profile", "core", "--apply"]), {
      apply: true,
      repo,
      profile: "core",
    });
  });
});

/**
 * Runs main over the fake API, with some routes answered differently.
 * @param {string[]} argv
 * @param {ReturnType<typeof matchingState>} state
 * @param {Record<string, import("./github-settings.mjs").ApiResponse>} routes keyed by "METHOD path"
 */
function runWith(argv, state, routes) {
  const { api, calls } = fakeApi(state);
  /** @type {string[]} */
  const lines = [];
  const code = main(
    argv,
    (method, path, body) => routes[`${method} ${path}`] ?? api(method, path, body),
    (line) => lines.push(line),
  );
  return { code, out: lines.join("\n"), calls };
}

describe("reading the live repo", () => {
  const r = `repos/${repo}`;

  test("private reporting refused on a private repo waits for public, it isn't drift or an error", () => {
    const state = matchingState("core");
    state.repo.private = true;
    const result = runWith([repo, "--profile", "core"], state, {
      [`GET ${r}/private-vulnerability-reporting`]: { status: 404, data: { message: "Not Found" } },
    });
    assert.equal(result.code, 0, result.out);
    assert.match(result.out, /waiting for public {2}security\.private_vulnerability_reporting/);
  });

  test("readLive marks the ruleset, private reporting, and default setup needs-public on a private repo", () => {
    const state = matchingState("core");
    state.repo.private = true;
    const { api } = fakeApi(state);
    const live = readLive(
      (method, path, body) =>
        path === `${r}/private-vulnerability-reporting`
          ? { status: 403, data: {} }
          : api(method, path, body),
      repo,
      profile("core"),
    );
    assert.equal(live.rulesets, "needs-public");
    assert.equal(live.privateVulnerabilityReporting, "needs-public");
    assert.equal(live.codeScanningDefaultSetup, "needs-public");
  });

  for (const [what, route, status, message] of /** @type {[string, string, number, RegExp][]} */ ([
    [
      "a 403 on rulesets",
      `GET ${r}/rulesets?includes_parents=false`,
      403,
      /listing rulesets failed: HTTP 403/,
    ],
    [
      "a 404 on private reporting",
      `GET ${r}/private-vulnerability-reporting`,
      404,
      /reading private vulnerability reporting failed: HTTP 404/,
    ],
    [
      "a 403 on default setup",
      `GET ${r}/code-scanning/default-setup`,
      403,
      /reading CodeQL default setup failed: HTTP 403/,
    ],
  ])) {
    test(`${what} on a public repo is an error (exit 2), never "waiting for public"`, () => {
      const result = runWith([repo, "--profile", "core"], matchingState("core"), {
        [route]: { status, data: { message: "no" } },
      });
      assert.equal(result.code, 2, result.out);
      assert.match(result.out, message);
      assert.doesNotMatch(result.out, /waiting for public/);
    });
  }

  test("a Dependabot alerts read that is neither 204 nor 404 is an error (exit 2)", () => {
    const result = runWith([repo, "--profile", "core"], matchingState("core"), {
      [`GET ${r}/vulnerability-alerts`]: {
        status: 403,
        data: { message: "Must have admin rights" },
      },
    });
    assert.equal(result.code, 2);
    assert.match(result.out, /reading Dependabot alerts failed: HTTP 403 .*admin rights/);
  });

  test("a repo that can't be read exits 2 and names it", () => {
    const result = runWith([repo, "--profile", "core"], matchingState("core"), {
      [`GET ${r}`]: { status: 404, data: { message: "Not Found" } },
    });
    assert.equal(result.code, 2);
    assert.match(result.out, /reading orvanohq\/orvano failed: HTTP 404/);
  });

  test("with Discussions off, core reports has_discussions as drift and doesn't query the categories", () => {
    const state = matchingState("core");
    state.repo.has_discussions = false;
    const result = run([repo, "--profile", "core"], state);
    assert.equal(result.code, 1);
    assert.match(result.out, /drift {2}repo\.has_discussions: want true, got false/);
    assert.doesNotMatch(result.out, /discussions\.categories/);
    assert.equal(
      result.calls.some((c) => c.path === "graphql"),
      false,
    );
  });

  test("the GraphQL query asks for this repo's owner and name", () => {
    const result = run([repo, "--profile", "core"], matchingState("core"));
    const query = result.calls.find((c) => c.path === "graphql");
    assert.deepEqual(query?.body.variables, { owner: "orvanohq", name: "orvano" });
  });
});

// ghApi runs the real `gh` binary, so these tests put a fake `gh` (a shell script) first on PATH.
// PATH holds only the fake's folder (or an empty one), so a real gh on the machine is never found. POSIX only:
// Windows can't run a shebang script as `gh`.
describe("ghApi", { skip: process.platform === "win32" }, () => {
  const dir = mkdtempSync(join(tmpdir(), "fake-gh-"));
  const bin = join(dir, "bin");
  const empty = join(dir, "empty");
  const stdout = join(dir, "stdout");
  const stderr = join(dir, "stderr");
  const argsFile = join(dir, "args");
  const stdinFile = join(dir, "stdin");
  mkdirSync(bin);
  mkdirSync(empty);
  const gh = join(bin, "gh");
  writeFileSync(
    gh,
    `#!/bin/sh
printf '%s\\n' "$@" > "${argsFile}"
/bin/cat > "${stdinFile}"
/bin/cat "${stderr}" >&2
/bin/cat "${stdout}"
`,
  );
  chmodSync(gh, 0o755);
  const savedPath = process.env.PATH;

  after(() => {
    process.env.PATH = savedPath;
    rmSync(dir, { recursive: true, force: true });
  });

  /**
   * Calls ghApi with the fake gh printing `out` (and `err` on stderr).
   * @param {string} out
   * @param {{ err?: string, method?: string, path?: string, body?: unknown, path_env?: string }} options
   */
  function call(out, { err = "", method = "GET", path = "repos/o/r", body, path_env = bin } = {}) {
    writeFileSync(stdout, out);
    writeFileSync(stderr, err);
    writeFileSync(argsFile, "");
    writeFileSync(stdinFile, "");
    process.env.PATH = path_env;
    try {
      return ghApi(method, path, body);
    } finally {
      process.env.PATH = savedPath;
    }
  }

  test("parses the status line and the JSON body", () => {
    const response = call(
      'HTTP/2.0 200 OK\nContent-Type: application/json\n\n{"private":true,"name":"r"}',
    );
    assert.deepEqual(response, { status: 200, data: { private: true, name: "r" } });
  });

  test("an empty 204 body is null data", () => {
    assert.deepEqual(call("HTTP/2.0 204 No Content\nX-GitHub-Request-Id: 1\n\n"), {
      status: 204,
      data: null,
    });
  });

  test("a status line with headers only and no blank line is still read", () => {
    assert.deepEqual(call("HTTP/1.1 404 Not Found\nContent-Length: 0"), {
      status: 404,
      data: null,
    });
  });

  test("a body that isn't JSON comes back as text", () => {
    assert.deepEqual(call("HTTP/2.0 502 Bad Gateway\n\n<html>oops</html>\n"), {
      status: 502,
      data: "<html>oops</html>",
    });
  });

  test("CRLF line endings are read like LF", () => {
    assert.deepEqual(call('HTTP/1.1 403 Forbidden\r\nA: b\r\n\r\n{"message":"no"}\r\n'), {
      status: 403,
      data: { message: "no" },
    });
  });

  test("an error status is returned, not thrown, so callers decide (needs public, 404 alerts)", () => {
    assert.equal(call('HTTP/2.0 403 Forbidden\n\n{"message":"Upgrade"}').status, 403);
  });

  test("sends the method, --include, the API version headers, and the path", () => {
    call("HTTP/2.0 200 OK\n\n{}", { method: "PATCH", path: "repos/o/r/labels/a%20b" });
    const args = readFileSync(argsFile, "utf8").trimEnd().split("\n");
    assert.deepEqual(args, [
      "api",
      "--method",
      "PATCH",
      "--include",
      "-H",
      "Accept: application/vnd.github+json",
      "-H",
      "X-GitHub-Api-Version: 2022-11-28",
      "repos/o/r/labels/a%20b",
    ]);
    assert.equal(readFileSync(stdinFile, "utf8"), "");
  });

  test("a body goes as JSON on stdin with --input -", () => {
    call("HTTP/2.0 201 Created\n\n{}", { method: "POST", body: { name: "bug", color: "d73a4a" } });
    const args = readFileSync(argsFile, "utf8").trimEnd().split("\n");
    assert.deepEqual(args.slice(-2), ["--input", "-"]);
    assert.deepEqual(JSON.parse(readFileSync(stdinFile, "utf8")), {
      name: "bug",
      color: "d73a4a",
    });
  });

  test("no HTTP status line (not logged in) throws a SettingsError with the login fix and gh's stderr", () => {
    assert.throws(
      () => call("", { err: "To get started with GitHub CLI, please run:  gh auth login" }),
      (error) =>
        error instanceof SettingsError &&
        /gh auth status/.test(error.message) &&
        /please run: {2}gh auth login/.test(error.message),
    );
  });

  test("a missing gh throws a SettingsError with the install link", () => {
    assert.throws(
      () => call("", { path_env: empty }),
      (error) => error instanceof SettingsError && /https:\/\/cli\.github\.com/.test(error.message),
    );
  });

  test("main turns a missing gh into exit 2 with the fix", () => {
    process.env.PATH = empty;
    /** @type {string[]} */
    const lines = [];
    let code;
    try {
      code = main([repo, "--profile", "core"], ghApi, (line) => lines.push(line));
    } finally {
      process.env.PATH = savedPath;
    }
    assert.equal(code, 2);
    assert.match(lines.join("\n"), /isn't installed or isn't on PATH/);
  });
});

describe("unexpected API answers are errors (exit 2), never drift (exit 1)", () => {
  const r = `repos/${repo}`;

  test("a GraphQL error for the Discussions categories exits 2 with the reason", () => {
    const result = runWith([repo, "--profile", "core"], matchingState("core"), {
      "POST graphql": {
        status: 200,
        data: { data: null, errors: [{ message: "Resource not accessible by integration" }] },
      },
    });
    assert.equal(result.code, 2, result.out);
    assert.match(result.out, /Reading Discussions categories failed: .*Resource not accessible/);
  });

  test("a GraphQL answer without the categories exits 2", () => {
    const result = runWith([repo, "--profile", "core"], matchingState("core"), {
      "POST graphql": { status: 200, data: { data: { repository: null } } },
    });
    assert.equal(result.code, 2, result.out);
  });

  test("a rulesets list that isn't a list exits 2", () => {
    const result = runWith([repo, "--profile", "core"], matchingState("core"), {
      [`GET ${r}/rulesets?includes_parents=false`]: { status: 200, data: { message: "odd" } },
    });
    assert.equal(result.code, 2, result.out);
    assert.match(result.out, /listing rulesets returned .*not a list/);
  });

  test("a labels list that isn't a list exits 2", () => {
    const result = runWith([repo, "--profile", "core"], matchingState("core"), {
      [`GET ${r}/labels?per_page=100`]: { status: 200, data: null },
    });
    assert.equal(result.code, 2, result.out);
  });
});
