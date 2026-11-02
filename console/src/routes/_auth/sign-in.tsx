import { createFileRoute } from "@tanstack/react-router";
import { requireSignedOut, safeRedirect } from "@/features/session";
import { SignInPage } from "@/features/session/components/sign-in-page";

export const Route = createFileRoute("/_auth/sign-in")({
  // The key is always set: the router lays this over the raw search, so a key left out would keep its raw value.
  validateSearch: (search: Record<string, unknown>): { redirect?: string | undefined } => ({
    redirect: safeRedirect(search.redirect),
  }),
  beforeLoad: requireSignedOut,
  component: SignInPage,
});
