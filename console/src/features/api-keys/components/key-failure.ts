import { t } from "@lingui/core/macro";
import { ApiError } from "@/shared/api/errors";

/** Spec 0017 AKY-5, LOC-4: what a refused key change says, or `fallback` for any other failure. */
export function keyFailure(error: unknown, fallback: string): string {
  return error instanceof ApiError && error.code === "project_not_active"
    ? t`This project is being deleted, so it can't change.`
    : fallback;
}
