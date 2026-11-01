import { del } from "@/shared/api/client";
import { config } from "@/shared/config";

// Spec 0003 QRY-9: what one tab tells the others. Never anything secret: the other tabs read the rest from the API.
const SIGNED_OUT = "signed-out";
const SESSION_CHANGED = "session-changed";

let channel: BroadcastChannel | undefined;

function sessionChannel(): BroadcastChannel {
  channel ??= new BroadcastChannel("orvano:session");
  return channel;
}

/** QRY-9: a full page load, which drops the query cache, every store, and all in memory state in one step. */
export function reloadConsole(path = ""): void {
  window.location.assign(`${config.basePath}/${path}`);
}

/** Spec 0017 SES-6, spec 0003 QRY-9: ends the session, then every tab goes to sign in. */
export async function signOutEverywhere(): Promise<void> {
  try {
    await del("/v1/console/session", {});
  } finally {
    // oxlint-disable-next-line unicorn/require-post-message-target-origin -- a BroadcastChannel has no target origin
    sessionChannel().postMessage(SIGNED_OUT);
    reloadConsole("sign-in");
  }
}

/** The cookie now holds a new session, so the other tabs' CSRF token is stale (spec 0017 ACC-2, SES-9). */
export function announceSessionChanged(): void {
  // oxlint-disable-next-line unicorn/require-post-message-target-origin -- a BroadcastChannel has no target origin
  sessionChannel().postMessage(SESSION_CHANGED);
}

/** Boot's step: this tab follows what the others do (AC-6). */
export function connectSessionChannel(onSessionChanged: () => void): void {
  sessionChannel().addEventListener("message", ({ data }) => {
    if (data === SIGNED_OUT) reloadConsole("sign-in");
    if (data === SESSION_CHANGED) onSessionChanged();
  });
}
