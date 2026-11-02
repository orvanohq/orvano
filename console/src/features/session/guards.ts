import { redirect, type ParsedLocation } from "@tanstack/react-router";
import type { QueryClient } from "@tanstack/react-query";
import { sessionQuery } from "./api/queries";

/** Spec 0003 RTE-9: the shell needs a session; a signed out visit goes to sign in and comes back. */
export async function requireSession({
  context,
  location,
}: {
  context: { queryClient: QueryClient };
  location: ParsedLocation;
}) {
  const session = await context.queryClient.query(sessionQuery());
  if (!session) {
    redirect({ to: "/sign-in", search: { redirect: location.href }, replace: true, throw: true });
  }
}

/** Spec 0017 CNS-2: a signed in operator has no business on `/sign-in`. */
export async function requireSignedOut({ context }: { context: { queryClient: QueryClient } }) {
  if (await context.queryClient.query(sessionQuery())) {
    redirect({ to: "/", replace: true, throw: true });
  }
}

/**
 * Only a path on this origin may come back from `?redirect=` (spec 0017 AC-6, spec 0003 URL-5). URL parsers drop tabs and
 * line breaks, so `/<tab>/example.com` would read as `//example.com`.
 */
export function safeRedirect(value: unknown): string | undefined {
  return typeof value === "string" && /^\/(?![/\\])[^\t\n\r]*$/.test(value) ? value : undefined;
}
