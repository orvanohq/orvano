import { createFileRoute, Outlet } from "@tanstack/react-router";
import { organizationQuery } from "@/features/organizations";
import { ShellRouteError } from "@/features/shell/components/shell-route-error";
import { frameData } from "@/shared/api/placeholder";

export const Route = createFileRoute("/_shell/$orgId")({
  // Spec 0003 RTE-9: `useCan` reads the organization's allowed actions from the context.
  beforeLoad: async ({ context, params }) => ({
    organization: await frameData(context.queryClient, organizationQuery(params.orgId)),
  }),
  component: Outlet,
  errorComponent: ShellRouteError,
});
