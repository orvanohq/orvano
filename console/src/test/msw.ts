import { setupWorker } from "msw/browser";
import { defaultHandlers } from "./handlers";

/**
 * Spec 0003 QA-2: the API is mocked with MSW's browser worker, its script served by MSW's Vite plugin
 * (vitest.config.ts). Tests add handlers with `worker.use(...)`; `src/test/setup-msw.ts` starts it and resets it after
 * each test.
 */
export const worker = setupWorker(...defaultHandlers);
