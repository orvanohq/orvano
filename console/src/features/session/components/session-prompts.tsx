import { lazy, Suspense } from "react";
import { useStore } from "@/shared/stores/create-store";
import { reauthPrompt, sessionEnded } from "@/shared/stores/session";

const ReauthDialog = lazy(() => import("./reauth-dialog"));
const SessionExpiredDialog = lazy(() => import("./session-expired-dialog"));

/**
 * Spec 0017 SES-8, SES-9: the shell's password prompt and its sign in dialog, over whatever asked for them. BDL-1: each
 * dialog loads when it's needed.
 */
export function SessionPrompts() {
  const isAsking = useStore(reauthPrompt, (waiting) => waiting.length > 0);
  const hasEnded = useStore(sessionEnded, (ended) => ended);
  return (
    <Suspense fallback={null}>
      {isAsking ? <ReauthDialog /> : null}
      {hasEnded ? <SessionExpiredDialog /> : null}
    </Suspense>
  );
}
