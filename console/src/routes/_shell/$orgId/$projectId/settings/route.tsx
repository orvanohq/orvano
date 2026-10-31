import { msg } from "@lingui/core/macro";
import { createFileRoute, Outlet } from "@tanstack/react-router";

export const Route = createFileRoute("/_shell/$orgId/$projectId/settings")({
  staticData: { crumb: msg`Settings` },
  component: Outlet,
});
