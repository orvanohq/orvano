import { createFileRoute } from "@tanstack/react-router";
import { requireSignedOut, safeRedirect } from "@/features/session";
import { SignInPage } from "@/features/session/components/sign-in-page";

export const Route = createFileRoute("/_auth/sign-in")({
  validateSearch: (search: Record<string, unknown>): { redirect?: string } => {
    const redirect = safeRedirect(search.redirect);
    return redirect ? { redirect } : {};
  },
  beforeLoad: requireSignedOut,
  component: SignInPage,
});
