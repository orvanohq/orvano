import { msg } from "@lingui/core/macro";
import { createFileRoute } from "@tanstack/react-router";
import { ProjectSettings } from "@/features/projects/components/project-settings";

export const Route = createFileRoute("/_shell/$orgId/$projectId/settings")({
  staticData: { crumb: msg`Settings` },
  component: ProjectSettings,
});
