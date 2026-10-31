import { t } from "@lingui/core/macro";
import { ApiError } from "@/shared/api/errors";

/** Spec 0001 UX-22: what a refused save says, by what the server said. */
export function saveFailure(error: unknown): string {
  if (error instanceof ApiError && error.code === "version_conflict") {
    return t`Someone else changed this project. Reload the page, then try again.`;
  }
  if (error instanceof ApiError && error.code === "project_not_active") {
    return t`This project is being deleted, so it can't change.`;
  }
  return t`Your changes weren't saved. Try again in a moment.`;
}
