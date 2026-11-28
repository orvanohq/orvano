"use client";

import { useSearchParams } from "next/navigation";
import { useSyncExternalStore } from "react";

/**
 * A loose project id check until features 7 and 14 own the real `ProjectId` format and tighten it.
 * Project addresses carry the id as `?project=` (spec 0003, Decision).
 */
export const projectIdPattern = /^prj_[0-9A-Za-z]{1,64}$/;

export type ProjectIdState =
  | { status: "loading" }
  | { status: "valid"; id: string }
  | { status: "invalid" };

const noSubscription = () => () => {};

/**
 * Reads the project id from `?project=`. When the parameter appears twice the first one wins.
 * It reports `loading` until the page has hydrated, so the prerendered HTML never shows a project.
 * `useSearchParams` needs a Suspense boundary around the component that calls this.
 */
export function useProjectId(): ProjectIdState {
  const params = useSearchParams();
  const hydrated = useSyncExternalStore(
    noSubscription,
    () => true,
    () => false,
  );

  if (!hydrated) {
    return { status: "loading" };
  }
  const id = params.get("project");
  return id !== null && projectIdPattern.test(id)
    ? { status: "valid", id }
    : { status: "invalid" };
}

/** Builds a project page link that carries the project forward. Every service link goes through this. */
export function projectHref(route: string, id: string): string {
  return `${route}?${new URLSearchParams({ project: id }).toString()}`;
}
