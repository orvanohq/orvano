import { afterAll, afterEach, beforeAll } from "vitest";
// Through the alias, as tests import it, so both get the same module and one worker.
import { worker } from "@/test/msw";

// Spec 0003 QA-2: a request to an API path with no handler fails the test; Vite's and Vitest's own requests pass
// through.
const unhandled: string[] = [];

beforeAll(async () => {
  await worker.start({
    quiet: true,
    onUnhandledFrame({ frame }) {
      if (frame.protocol !== "http") return;
      const { request } = frame.data as { request: Request };
      const url = new URL(request.url);
      if (url.pathname.startsWith("/v1/")) unhandled.push(`${request.method} ${url.pathname}`);
    },
  });
});

afterEach(() => {
  worker.resetHandlers();
  const requests = unhandled.splice(0);
  if (requests.length > 0)
    throw new Error(`API requests with no MSW handler: ${requests.join(", ")}`);
});

afterAll(async () => {
  await worker.stop();
});
