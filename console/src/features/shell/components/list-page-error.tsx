import { useEffect } from "react";
import { useLingui } from "@lingui/react/macro";
import { useNavigate, type ErrorComponentProps } from "@tanstack/react-router";
import { ApiError } from "@/shared/api/errors";
import { notify } from "@/ui";
import { ShellRouteError } from "./shell-route-error";

/**
 * Spec 0003 URL-4, spec 0001 UX-27: a paged list that failed to load. A cursor the API no longer accepts falls back to
 * the first page with a toast, never an error page. BDL-1: here rather than in the loader, so it loads on demand.
 */
export function ListPageError(props: ErrorComponentProps) {
  const { t } = useLingui();
  const navigate = useNavigate();
  const { error } = props;
  const isBadCursor = error instanceof ApiError && error.code === "invalid_cursor";

  useEffect(() => {
    if (!isBadCursor) return;
    notify({
      tone: "info",
      message: t`This link's page expired, showing the first page`,
      source: t`Pagination`,
    });
    void navigate({
      to: ".",
      search: (previous: object) => ({ ...previous, cursor: undefined }),
      replace: true,
    } as never);
  }, [isBadCursor, navigate, t]);

  return isBadCursor ? null : <ShellRouteError {...props} />;
}
