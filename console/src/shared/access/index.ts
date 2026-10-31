import { useRouteContext } from "@tanstack/react-router";

/** A console resource: its payload lists what the operator may do with it (spec 0014 CPX-1). */
export interface WithAllowedActions {
  allowedActions: readonly string[];
}

/** The console permissions a screen asks about (spec 0012 CPL-1). */
export type ConsoleAction =
  | "org.write"
  | "projects.create"
  | "project.write"
  | "project.delete"
  | "keys.write";

type ResourceKind = "organization" | "project" | "api_key";

/**
 * Spec 0003 RTE-9, spec 0001 UX-30: whether the operator may take `action`, from the resource's `allowedActions`. Without
 * a `resource` it reads the organization or project the route put in its context. Nothing here knows a role.
 */
export function useCan(kind: ResourceKind, action: ConsoleAction, resource?: WithAllowedActions) {
  const context: { organization?: WithAllowedActions; project?: WithAllowedActions } =
    useRouteContext({ strict: false });
  const target = resource ?? (kind === "api_key" ? undefined : context[kind]);
  return target?.allowedActions.includes(action) ?? false;
}
