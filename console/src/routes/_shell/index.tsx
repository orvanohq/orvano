import { createFileRoute, redirect } from "@tanstack/react-router";
import { sessionQuery } from "@/features/session";
import { NoOrganization } from "@/features/shell/components/no-organization";

// Spec 0017 CNS-1: the console root is the session's first organization.
export const Route = createFileRoute("/_shell/")({
  beforeLoad: async ({ context }) => {
    const session = await context.queryClient.query(sessionQuery());
    const first = session?.memberships[0];
    if (first) {
      redirect({
        to: "/$orgId",
        params: { orgId: first.organizationId },
        replace: true,
        throw: true,
      });
    }
  },
  component: NoOrganization,
});
