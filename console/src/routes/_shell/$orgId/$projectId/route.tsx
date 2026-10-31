import { createFileRoute, Outlet } from "@tanstack/react-router";
import { projectQuery } from "@/features/projects";
import { ShellRouteError } from "@/features/shell/components/shell-route-error";

export const Route = createFileRoute("/_shell/$orgId/$projectId")({
  // Spec 0003 RTE-9: `useCan` reads the project's allowed actions from the context.
  beforeLoad: async ({ context, params }) => ({
    project: await context.queryClient.query(projectQuery(params.projectId)),
  }),
  component: Outlet,
  errorComponent: ShellRouteError,
});
