import { msg } from "@lingui/core/macro";
import { createFileRoute } from "@tanstack/react-router";
import { apiKeysQuery } from "@/features/api-keys";
import { KeysPage } from "@/features/api-keys/components/keys-page";
import { ListPageError } from "@/features/shell/components/list-page-error";
import { toListPage, validatePageSearch } from "@/shared/url/paging";

export const Route = createFileRoute("/_shell/$orgId/$projectId/settings/keys")({
  staticData: { crumb: msg`API keys` },
  validateSearch: validatePageSearch,
  loaderDeps: ({ search }) => toListPage(search),
  loader: ({ context, params, deps }) =>
    context.queryClient.query(apiKeysQuery(params.projectId, deps)),
  component: KeysPage,
  errorComponent: ListPageError,
});
