import { createFileRoute, redirect } from "@tanstack/react-router";

// Spec 0017 CNS-1: the first settings page is General.
export const Route = createFileRoute("/_shell/$orgId/$projectId/settings/")({
  beforeLoad: ({ params }) => {
    redirect({ to: "/$orgId/$projectId/settings/general", params, replace: true, throw: true });
  },
});
