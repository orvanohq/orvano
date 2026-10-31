import { msg } from "@lingui/core/macro";
import { createFileRoute } from "@tanstack/react-router";
import { OriginsSettings } from "@/features/projects/components/origins-settings";

export const Route = createFileRoute("/_shell/$orgId/$projectId/settings/origins")({
  staticData: { crumb: msg`Allowed origins` },
  component: OriginsSettings,
});
