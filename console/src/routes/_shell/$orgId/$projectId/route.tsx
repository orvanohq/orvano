import { createFileRoute, Outlet } from "@tanstack/react-router";
import { projectQuery } from "@/features/projects";
import { ShellRouteError } from "@/features/shell/components/shell-route-error";

export const Route = createFileRoute("/_shell/$orgId/$projectId")({
  loader: ({ context, params }) => context.queryClient.query(projectQuery(params.projectId)),
  component: Outlet,
  errorComponent: ShellRouteError,
});
