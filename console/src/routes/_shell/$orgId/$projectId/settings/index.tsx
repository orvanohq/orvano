import { createFileRoute, redirect } from "@tanstack/react-router";

// Spec 0017 CNS-1: the first settings page. It becomes General with build plan step 17.
export const Route = createFileRoute("/_shell/$orgId/$projectId/settings/")({
  beforeLoad: ({ params }) => {
    redirect({ to: "/$orgId/$projectId/settings/keys", params, replace: true, throw: true });
  },
});
