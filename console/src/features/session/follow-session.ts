import type { QueryClient } from "@tanstack/react-query";
import { get } from "@/shared/api/client";
import { reloadConsole } from "@/shared/session-channel";
import { sessionEnded } from "@/shared/stores/session";
import { sessionQuery } from "./api/queries";

/**
 * Spec 0017 ACC-2, SES-9: another tab changed the session, so this one reads it again for the new CSRF token. If someone
 * else signed in, the page holds the first operator's data (QRY-8). A failed read leaves the tab as it is.
 */
export async function followSession(queryClient: QueryClient): Promise<void> {
  const held = queryClient.getQueryData(sessionQuery().queryKey);
  if (!held) return;
  const session = await get("/v1/console/session", { signal: new AbortController().signal });
  if (session.operator.id !== held.operator.id) {
    reloadConsole();
    return;
  }
  queryClient.setQueryData(sessionQuery().queryKey, session);
  sessionEnded.set(false);
}
