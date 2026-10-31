import { msg } from "@lingui/core/macro";
import { createFileRoute } from "@tanstack/react-router";
import { apiKeysQuery } from "@/features/api-keys";
import { KeysPage } from "@/features/api-keys/components/keys-page";

export const Route = createFileRoute("/_shell/$orgId/$projectId/settings/keys")({
  staticData: { crumb: msg`API keys` },
  loader: ({ context, params }) => context.queryClient.query(apiKeysQuery(params.projectId)),
  component: KeysPage,
});
