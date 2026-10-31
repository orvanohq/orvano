import { useNavigate, useSearch } from "@tanstack/react-router";

/** Spec 0003 URL-3: the organization page's search state: which create dialog is open, so a switcher can link to it. */
export function useOrganizationView() {
  const { create: creating } = useSearch({ from: "/_shell/$orgId/" });
  const navigate = useNavigate({ from: "/$orgId/" });
  return {
    creating,
    open: (create?: "project" | "organization") => {
      void navigate({ search: { create }, replace: true });
    },
  };
}
