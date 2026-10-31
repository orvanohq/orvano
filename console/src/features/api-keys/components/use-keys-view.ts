import { useSearch } from "@tanstack/react-router";
import { usePaging } from "@/shared/url/use-paging";
import { toListPage } from "@/shared/url/paging";

/** Spec 0003 URL-3: the keys page's search state, which is the page it's on. */
export function useKeysView() {
  const search = useSearch({ from: "/_shell/$orgId/$projectId/settings/keys" });
  return { page: toListPage(search), paging: usePaging(search) };
}
