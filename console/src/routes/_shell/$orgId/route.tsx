import { createFileRoute, Outlet } from "@tanstack/react-router";
import { organizationQuery } from "@/features/organizations";
import { ShellRouteError } from "@/features/shell/components/shell-route-error";

export const Route = createFileRoute("/_shell/$orgId")({
  loader: ({ context, params }) => context.queryClient.query(organizationQuery(params.orgId)),
  component: Outlet,
  errorComponent: ShellRouteError,
});
