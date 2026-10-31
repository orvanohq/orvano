// Spec 0014 CNF-4, STB-2: a reference runner. It renders ts-client and ts-server from the real contract and
// scenarios, then runs every stub scenario against the stub server, in process. Live scenarios run too when
// ORVANO_CONFORMANCE_URL points at a server (api/conformance/compose.yaml serves one on http://localhost:18080), and
// those that require a project when ORVANO_CONFORMANCE_PROJECT holds the JSON of `orvano admin bootstrap-project` (CNF-7).
// The runners in sdk-js and sdk-js-server follow this one.
import { mkdtempSync, rmSync } from "node:fs";
import type { Server } from "node:http";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { afterAll, beforeAll, describe, expect, it } from "vitest";
import { loadConformance, type Scenario } from "../src/conformance.ts";
import { loadContract } from "../src/contract.ts";
import { generate } from "../src/generate.ts";
import { writeOutput } from "../src/output.ts";
import { findTarget } from "../src/targets/index.ts";
// @ts-expect-error the stub is plain JavaScript with JSDoc types, outside this project's include
import { createStubServer } from "../../conformance/stub/server.mjs";

const here = dirname(fileURLToPath(import.meta.url));
const api = join(here, "..", "..");
const conformance = loadConformance(join(api, "conformance"));
const contract = loadContract(join(api, "openapi.json"));
const liveUrl = process.env["ORVANO_CONFORMANCE_URL"];
const projectId = "0192f0c1-0000-7000-8000-000000000001";
const key = "conformance-key";
const bootstrap = process.env["ORVANO_CONFORMANCE_PROJECT"];
const project =
  bootstrap === undefined
    ? undefined
    : (JSON.parse(bootstrap) as { projectId: string; publishableKey: string; secretKey: string });

interface Dispatcher {
  dispatch: (
    client: unknown,
    call: string,
    params: Record<string, unknown>,
    options: Record<string, unknown>,
  ) => Promise<{ ok: true; result: unknown } | { ok: false; error: unknown }>;
  match: (expected: unknown, actual: unknown, path?: string) => string[];
  substitute: (value: unknown, variables: Record<string, string>) => unknown;
}

interface Sdk {
  dispatcher: Dispatcher;
  apiVersion: string;
  createClientHandle(
    options: Record<string, unknown>,
    clientName: string,
    credentials: Record<string, string>,
  ): unknown;
}

const root = mkdtempSync(join(tmpdir(), "orvano-conformance-"));
let stub: Server;
let stubUrl = "";

async function sdkFor(target: "ts-client" | "ts-server"): Promise<Sdk> {
  const out = join(root, target);
  writeOutput(
    out,
    findTarget(target).ownership,
    generate(contract, conformance, target, "0".repeat(40)),
  );
  const load = async <T>(path: string): Promise<T> =>
    (await import(pathToFileURL(join(out, path)).href)) as T;
  return {
    dispatcher: await load<Dispatcher>("conformance/dispatch.generated.ts"),
    apiVersion: (await load<{ apiVersion: string }>("src/generated/contract.ts")).apiVersion,
    createClientHandle: (await load<Pick<Sdk, "createClientHandle">>("src/runtime/options.ts"))
      .createClientHandle,
  };
}

beforeAll(async () => {
  stub = (createStubServer as () => Server)();
  await new Promise<void>((resolve) => {
    stub.listen(0, "127.0.0.1", () => {
      resolve();
    });
  });
  const address = stub.address();
  if (address === null || typeof address === "string") throw new Error("The stub has no port.");
  stubUrl = `http://127.0.0.1:${String(address.port)}`;
});

afterAll(() => {
  stub.close();
  rmSync(root, { recursive: true, force: true });
});

/** Runs one scenario and returns every mismatch (CNF-3). */
async function run(sdk: Sdk, scenario: Scenario, clientName: string): Promise<string[]> {
  const token = `run-${Math.random().toString(36).slice(2, 12)}`;
  // SDK-4: a scenario that requires a project runs on the real one, with the key of this SDK's audience.
  const real = scenario.requires.includes("project") ? project : undefined;
  const audienceKey = clientName.startsWith("client/") ? real?.publishableKey : real?.secretKey;
  const openKey =
    typeof scenario.client?.["key"] === "string" ? scenario.client["key"] : audienceKey;
  const variables: Record<string, string> = {
    apiVersion: sdk.apiVersion,
    clientName,
    key: openKey ?? key,
    projectId: real?.projectId ?? projectId,
  };
  const { match, substitute, dispatch } = sdk.dispatcher;
  const url = scenario.mode === "stub" ? stubUrl : (liveUrl ?? "");
  if (scenario.mode === "stub") {
    const loaded = await fetch(`${stubUrl}/__stub/runs/${token}/scenarios/${scenario.id}`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ stub: substitute(scenario.stub, variables) }),
    });
    if (loaded.status !== 204) return [`the stub refused the scenario (${String(loaded.status)})`];
  }

  // The runner's own fetch keeps the last response, for `expect.response`.
  let last: { status: number; headers: Record<string, string>; body: unknown } | undefined;
  const capture = async (request: Request): Promise<Response> => {
    const response = await fetch(request);
    const text = await response.clone().text();
    let body: unknown = text;
    try {
      body = JSON.parse(text);
    } catch {
      // not JSON: keep the text
    }
    last = { status: response.status, headers: Object.fromEntries(response.headers), body };
    return response;
  };
  const client = sdk.createClientHandle(
    { url, project: variables["projectId"], fetch: capture },
    clientName,
    { "Orvano-Key": variables["key"] ?? key },
  );

  const problems: string[] = [];
  for (const [index, step] of scenario.steps.entries()) {
    const options = (substitute(step.options ?? {}, variables) ?? {}) as Record<string, unknown>;
    const headers: Record<string, string> = {
      ...(options["headers"] as Record<string, string> | undefined),
    };
    if (scenario.mode === "stub") headers["Orvano-Stub-Run"] = token;
    // oxlint-disable-next-line no-await-in-loop -- a scenario's steps run in order, each after the last
    const outcome = await dispatch(
      client,
      step.call,
      (substitute(step.params ?? {}, variables) ?? {}) as Record<string, unknown>,
      {
        ...options,
        headers,
      },
    );
    const expected = substitute(step.expect, variables) as Record<string, unknown>;
    const where = `step ${String(index + 1)}`;
    if ("result" in expected) {
      problems.push(
        ...(outcome.ok
          ? match(expected["result"], outcome.result, `${where} result`)
          : [`${where}: failed with ${JSON.stringify(outcome.error)}`]),
      );
    } else {
      problems.push(
        ...(outcome.ok
          ? [`${where}: succeeded, expected an error`]
          : match(expected["error"], outcome.error, `${where} error`)),
      );
    }
    if (expected["response"] !== undefined)
      problems.push(...match(expected["response"], last, `${where} response`));
  }

  if (scenario.mode === "stub") {
    const recorded = (await (await fetch(`${stubUrl}/__stub/runs/${token}/requests`)).json()) as {
      requests: unknown[];
    };
    problems.push(
      ...match(substitute(scenario.expectRequests, variables), recorded.requests, "requests"),
    );
  }
  return problems;
}

for (const [target, clientName] of [
  ["ts-client", "client/0.0.0-conformance"],
  ["ts-server", "server/0.0.0-conformance"],
] as const) {
  const audience = target === "ts-client" ? "client" : "server";
  const scenarios = conformance.scenarios.filter((scenario) =>
    scenario.audience.includes(audience),
  );

  describe(`0014 AC-17: ${target} runs every ${audience} scenario`, () => {
    let sdk: Sdk;
    beforeAll(async () => {
      sdk = await sdkFor(target);
    });

    for (const scenario of scenarios) {
      const skip =
        (scenario.mode === "live" && liveUrl === undefined) ||
        (scenario.requires.includes("project") && project === undefined);
      it.skipIf(skip)(`${scenario.id}: ${scenario.title}`, async () => {
        expect(await run(sdk, scenario, clientName)).toEqual([]);
      });
    }
  });
}
