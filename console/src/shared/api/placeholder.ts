import type { QueryClient, QueryExecuteOptions, QueryKey } from "@tanstack/react-query";
import type { ApiError } from "./errors";

/**
 * Spec 0003 QRY-5, spec 0001 UX-21: a paged list keeps its rows on screen while the next page loads, and never shows
 * another resource's rows. A list's key holds the ID of the resource that owns it (QRY-2).
 */
export function keepPreviousForSameResource(resourceId: string) {
  return <T>(previous: T | undefined, query: { queryKey: readonly unknown[] } | undefined) =>
    query?.queryKey.includes(resourceId) ? previous : undefined;
}

/** What a paged list's `useQuery` adds: the rows stay up, and a page that can't load goes to the route's error component. */
export const keepListPage = (resourceId: string) => ({
  placeholderData: keepPreviousForSameResource(resourceId),
  throwOnError: (_error: unknown, query: { state: { data: unknown } }) =>
    query.state.data === undefined,
});

/**
 * Whether a list's loader runs for a move between pages of a list already on screen: the same route (`stay`), and a
 * query in the list's `filter` in use. The loader doesn't wait then (RTE-4): the router would hide the rows and their
 * pager behind the skeleton, and focus would drop to the document. Coming from anywhere else, another organization's
 * list too, it waits, so a slow list shows the skeleton (UX-21).
 */
export function isPageTurn(
  cause: "enter" | "stay" | "preload",
  queryClient: QueryClient,
  filter: { queryKey: QueryKey },
): boolean {
  return (
    cause === "stay" &&
    queryClient.getQueryCache().find({ ...filter, exact: false, type: "active" }) !== undefined
  );
}

/**
 * Spec 0003 RTE-4: a layout's frame data. Anything cached answers at once and refreshes behind the page: waiting for a
 * refresh would hold up every navigation on a slow link, and end a page turn in the skeleton.
 */
export function frameData<Data, Key extends QueryKey>(
  queryClient: QueryClient,
  options: QueryExecuteOptions<Data, ApiError, Data, Data, Key>,
): Promise<Data> {
  void queryClient.query(options).catch(() => undefined); // the wait below reports a failure that matters
  return queryClient.query({ ...options, staleTime: "static" });
}
