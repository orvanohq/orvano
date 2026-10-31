import { useNavigate, useSearch } from "@tanstack/react-router";
import { toListPage } from "@/shared/url/paging";
import { usePaging } from "@/shared/url/use-paging";

/**
 * Spec 0003 URL-3: the organization page's search state: the page it's on, and which create dialog is open, so a
 * switcher can link to it.
 */
export function useOrganizationView() {
  const search = useSearch({ from: "/_shell/$orgId/" });
  const navigate = useNavigate({ from: "/$orgId/" });
  return {
    creating: search.create,
    page: toListPage(search),
    paging: usePaging(search),
    open: (create?: "project" | "organization") => {
      void navigate({
        search: (previous) => ({ ...previous, create }),
        replace: true,
        state: true,
      });
    },
  };
}
