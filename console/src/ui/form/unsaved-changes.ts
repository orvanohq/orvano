import { createContext, useContext, useEffect, useId } from "react";
import { useLocation } from "@tanstack/react-router";
import { createStore, useStore } from "@/shared/stores/create-store";

// Spec 0003 RTE-8, spec 0001 UX-9: every form with unsaved changes registers here, and `UnsavedChangesGuard` asks
// one question for all of them. A form counts as changed while its values differ from the last saved ones (FRM-5).

interface Registration {
  /** The surface the form sits in (a side sheet), so closing that surface asks only about its own forms. */
  scope: string | undefined;
  /** Where the form was when it changed: leaving this path unmounts it. */
  pathname: string;
  /** Search params the form's surface depends on (`sheet`, `row`): changing one unmounts or swaps it. */
  watch: Readonly<Record<string, unknown>>;
}

const registrations = createStore<ReadonlyMap<string, Registration>>(new Map());

interface DiscardRequest {
  resolve: (discard: boolean) => void;
}

/** A question asked by a surface closing itself (a sheet's Escape), not by a navigation. */
const discardRequest = createStore<DiscardRequest | null>(null);

/** A surface that holds forms (a side sheet) and the search params that keep it open. */
export interface UnsavedScope {
  id: string;
  watch: readonly string[];
}

export const UnsavedScopeContext = createContext<UnsavedScope | undefined>(undefined);

/** Registers a form while `changed` is true. `Form` calls it; screens never do. */
export function useUnsavedChanges(changed: boolean): void {
  const id = useId();
  const scope = useContext(UnsavedScopeContext);
  const location = useLocation();
  const search = location.search as Record<string, unknown>;
  const watched = Object.fromEntries((scope?.watch ?? []).map((key) => [key, search[key]]));
  const pathname = location.pathname;
  const watchKey = JSON.stringify(watched);

  useEffect(() => {
    if (!changed) return;
    registrations.set((map) =>
      new Map(map).set(id, {
        scope: scope?.id,
        pathname,
        watch: JSON.parse(watchKey) as Record<string, unknown>,
      }),
    );
    return () => {
      forget((key) => key === id);
    };
  }, [changed, id, scope?.id, pathname, watchKey]);
}

function forget(match: (id: string, registration: Registration) => boolean): void {
  registrations.set((map) => {
    const next = new Map([...map].filter(([id, registration]) => !match(id, registration)));
    return next.size === map.size ? map : next;
  });
}

interface BlockLocation {
  pathname: string;
  search: unknown;
}

/** The changed forms a navigation would unmount: a different path, or a watched search param that changes. */
export function affectedBy(next: BlockLocation): string[] {
  const search = (next.search ?? {}) as Record<string, unknown>;
  return [...registrations.get()]
    .filter(
      ([, registration]) =>
        registration.pathname !== next.pathname ||
        Object.entries(registration.watch).some(([key, value]) => search[key] !== value),
    )
    .map(([id]) => id);
}

/** Drops registrations whose changes the user chose to discard, so the navigation that follows isn't asked again. */
export function discard(ids: readonly string[]): void {
  forget((id) => ids.includes(id));
}

export function hasUnsavedChanges(): boolean {
  return registrations.get().size > 0;
}

export function useHasUnsavedChanges(): boolean {
  return useStore(registrations, (map) => map.size > 0);
}

/**
 * Asks before a surface closes itself (a side sheet's Escape, Cancel, or close button). Resolves true at once when
 * none of the surface's forms has changes; otherwise the guard's dialog asks, and a discard forgets those forms.
 */
export function confirmDiscard(scope: string): Promise<boolean> {
  const ids = [...registrations.get()]
    .filter(([, registration]) => registration.scope === scope)
    .map(([id]) => id);
  if (ids.length === 0) return Promise.resolve(true);
  return new Promise((resolve) => {
    discardRequest.get()?.resolve(false);
    discardRequest.set({
      resolve: (confirmed) => {
        discardRequest.set(null);
        if (confirmed) discard(ids);
        resolve(confirmed);
      },
    });
  });
}

export function useDiscardRequest(): DiscardRequest | null {
  return useStore(discardRequest, (request) => request);
}

/** Test only: forgets every registration and any open question. */
export function resetUnsavedChangesForTests(): void {
  registrations.set(new Map());
  discardRequest.set(null);
}
