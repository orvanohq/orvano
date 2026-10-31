import { compose } from "./stack";

// Spec 0003 QA-5: a clean stack for every run, gone again in the teardown it returns.
// oxlint-disable-next-line import/no-default-export -- Playwright takes globalSetup as a default export
export default function globalSetup() {
  compose("down", "--volumes", "--remove-orphans");
  compose("up", "--detach", "--wait", "--wait-timeout", "180");
  return () => {
    if (!process.env.ORVANO_E2E_KEEP) compose("down", "--volumes", "--remove-orphans");
  };
}
