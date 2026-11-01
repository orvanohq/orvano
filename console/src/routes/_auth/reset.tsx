import { createFileRoute } from "@tanstack/react-router";
import { ResetPage } from "@/features/session/components/reset-page";

// Spec 0017 RST-2: no guard. Whoever holds a link may use it, signed in or not.
export const Route = createFileRoute("/_auth/reset")({
  component: ResetPage,
});
