import { msg } from "@lingui/core/macro";
import { createFileRoute } from "@tanstack/react-router";
import { GeneralSettings } from "@/features/projects/components/general-settings";

export const Route = createFileRoute("/_shell/$orgId/$projectId/settings/general")({
  staticData: { crumb: msg`General` },
  component: GeneralSettings,
});
