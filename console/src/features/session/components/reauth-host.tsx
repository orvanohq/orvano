import { lazy, Suspense } from "react";
import { useStore } from "@/shared/stores/create-store";
import { reauthPrompt } from "../reauth";

const ReauthDialog = lazy(() => import("./reauth-dialog"));

/** Spec 0017 SES-8: the shell's one password prompt, over whatever asked for it. BDL-1: the dialog loads when it's needed. */
export function ReauthHost() {
  const isAsking = useStore(reauthPrompt, (waiting) => waiting.length > 0);
  return isAsking ? (
    <Suspense fallback={null}>
      <ReauthDialog />
    </Suspense>
  ) : null;
}
