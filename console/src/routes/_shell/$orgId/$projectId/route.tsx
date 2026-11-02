import { createFileRoute, Outlet } from "@tanstack/react-router";
import { projectQuery } from "@/features/projects";
import { ShellRouteError } from "@/features/shell/components/shell-route-error";
import { frameData } from "@/shared/api/placeholder";

export const Route = createFileRoute("/_shell/$orgId/$projectId")({
  // Spec 0003 RTE-9: `useCan` reads the project's allowed actions from the context.
  beforeLoad: async ({ context, params }) => ({
    project: await frameData(context.queryClient, projectQuery(params.projectId)),
  }),
  component: Outlet,
  errorComponent: ShellRouteError,
});
