import { msg } from "@lingui/core/macro";
import { createFileRoute } from "@tanstack/react-router";
import { OrganizationSettings } from "@/features/organizations/components/organization-settings";

export const Route = createFileRoute("/_shell/$orgId/settings")({
  staticData: { crumb: msg`Organization settings` },
  component: OrganizationSettings,
});
