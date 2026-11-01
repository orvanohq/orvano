// Spec 0001 UX-14, UX-27: the page a list is on lives in the URL. Spec 0014 PAG-2: the API hands back only the next
// cursor, and a client keeps the ones it has seen.

export const PAGE_SIZES = [25, 50, 100] as const;
export const DEFAULT_PAGE_SIZE = 50;

export interface PageSearch {
  cursor?: string | undefined;
  size?: number | undefined;
}

/**
 * URL-1: a hand edited or stale URL falls back, and the default size stays out of the URL. Both keys are always set: the
 * router lays this over the raw search, so a key left out would keep its raw value. A plain function, as every route's
 * search is so far: Zod's coerce and catch cost 500 bytes of BDL-1's first load.
 */
export function validatePageSearch({ cursor, size }: Record<string, unknown>): PageSearch {
  return {
    cursor: typeof cursor === "string" && cursor.length <= 2048 ? cursor : undefined,
    size: size === 25 || size === 100 ? size : undefined,
  };
}

/** A list query's page: `loaderDeps` and the query key hold this and nothing else in the search. */
export interface ListPage {
  cursor?: string | undefined;
  limit: number;
}

export function toListPage({ cursor, size }: PageSearch): ListPage {
  return { cursor, limit: size ?? DEFAULT_PAGE_SIZE };
}
