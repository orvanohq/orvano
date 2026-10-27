import { useEffect } from "react";
import type { Crumb, ShellModel } from "./types";

/**
 * Spec 0001 UX-4: the crumbs in reverse, the org left out, then the brand: `Resource · Service · Project · Orvano`.
 */
export function documentTitle(
  crumbs: readonly Crumb[],
  model: Pick<ShellModel, "project">,
): string {
  const parts = crumbs
    .filter((crumb) => crumb.kind !== "org")
    .map((crumb) => (crumb.kind === "page" ? crumb.label : (model.project?.name ?? "")))
    .filter(Boolean)
    .toReversed();
  return [...parts, "Orvano"].join(" · ");
}

/** Spec 0015 SHL-1, UX-4: keeps `document.title` in step with the crumbs. */
export function useDocumentTitle(crumbs: readonly Crumb[], model: Pick<ShellModel, "project">) {
  const title = documentTitle(crumbs, model);
  useEffect(() => {
    document.title = title;
  }, [title]);
}
