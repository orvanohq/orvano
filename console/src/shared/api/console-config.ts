import type { components } from "./schema.gen";

/** What the console needs before its first render (spec 0014 CPX-3, spec 0016 FRS-1). */
export type ConsoleConfig = components["schemas"]["ConsoleConfigResponse"];

const CONFIG_PATH = "/v1/console/config";

/**
 * `GET /v1/console/config`, the boot call of spec 0003 RTE-10. The API is same origin, outside the `/console` base
 * path. The one client of QRY-1 (openapi-fetch, `ApiError` of FLT-1) takes this call over when the console makes its
 * first write (spec 0016 build plan step 13); until then a failure is a plain error, and boot shows the boot error
 * page for it (FRS-2).
 */
export async function getConsoleConfig(signal: AbortSignal): Promise<ConsoleConfig> {
  const response = await fetch(CONFIG_PATH, {
    headers: { Accept: "application/json" },
    signal: AbortSignal.any([signal, AbortSignal.timeout(30_000)]), // QRY-3
  });
  if (!response.ok) throw new Error(`GET ${CONFIG_PATH} answered ${String(response.status)}`);
  return (await response.json()) as ConsoleConfig;
}
