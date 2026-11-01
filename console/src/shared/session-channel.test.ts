import { describe, expect, it, vi } from "vitest";
import { announceSessionChanged, connectSessionChannel } from "./session-channel";

// Spec 0003 QRY-9, spec 0017 ACC-2: what tabs tell each other. Sign out's full page load is covered by the stack run.
describe("the session channel", () => {
  it("0017 ACC-2: follows a session another tab changed, and ignores what it doesn't know", async () => {
    const onSessionChanged = vi.fn<() => void>();
    connectSessionChannel(onSessionChanged);
    const otherTab = new BroadcastChannel("orvano:session");

    // oxlint-disable-next-line unicorn/require-post-message-target-origin -- a BroadcastChannel has no target origin
    otherTab.postMessage("something-else");
    // oxlint-disable-next-line unicorn/require-post-message-target-origin -- a BroadcastChannel has no target origin
    otherTab.postMessage("session-changed");

    await vi.waitFor(() => {
      expect(onSessionChanged).toHaveBeenCalledOnce();
    });
    otherTab.close();
  });

  it("0003 QRY-9: tells the other tabs, by name, and sends nothing secret", async () => {
    const otherTab = new BroadcastChannel("orvano:session");
    const seen: unknown[] = [];
    otherTab.addEventListener("message", ({ data }) => {
      seen.push(data);
    });

    announceSessionChanged();

    await vi.waitFor(() => {
      expect(seen).toEqual(["session-changed"]);
    });
    otherTab.close();
  });
});
