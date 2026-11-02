import { QueryClient, QueryObserver } from "@tanstack/react-query";
import { describe, expect, it, vi } from "vitest";
import {
  frameData,
  isPageTurn,
  keepListPage,
  keepPreviousForSameResource,
} from "@/shared/api/placeholder";
import { apiKeysFilter, apiKeysQuery } from "./queries";

const fetching = (queryFn: () => Promise<string>) => ({
  ...apiKeysQuery("a"),
  queryFn: queryFn as never,
});

// Spec 0003 RTE-4, QRY-5 on a real key: shared/api can't import a feature, and the shape of its keys is the point.
describe("keepPreviousForSameResource", () => {
  it("0003 QRY-5: keeps another page of the same resource", () => {
    const keep = keepPreviousForSameResource("a");

    expect(keep("rows", apiKeysQuery("a", { cursor: "page-2", limit: 50 }))).toBe("rows");
  });

  it("0001 UX-21: never shows another resource's rows", () => {
    const keep = keepPreviousForSameResource("b");

    expect(keep("rows", apiKeysQuery("a"))).toBeUndefined();
    expect(keep("rows", undefined)).toBeUndefined();
  });
});

describe("keepListPage", () => {
  it("0003 URL-4: a page with no data goes to the route's error component, and a failed refetch keeps the rows", () => {
    const { throwOnError } = keepListPage("a");

    expect(throwOnError(new Error("no"), { state: { data: undefined } })).toBe(true);
    expect(throwOnError(new Error("no"), { state: { data: "rows" } })).toBe(false);
  });
});

describe("isPageTurn", () => {
  it("0003 RTE-4: is true on the same route with a page of the list on screen, and at no other time", () => {
    const queryClient = new QueryClient();
    const unsubscribe = new QueryObserver(queryClient, {
      ...apiKeysQuery("a"),
      staleTime: Number.POSITIVE_INFINITY,
      initialData: { items: [], nextCursor: null, total: null },
    } as never).subscribe(() => undefined);

    expect(isPageTurn("stay", queryClient, apiKeysFilter("a"))).toBe(true);
    expect(isPageTurn("enter", queryClient, apiKeysFilter("a"))).toBe(false); // the overview shows the first page
    expect(isPageTurn("preload", queryClient, apiKeysFilter("a"))).toBe(false);
    expect(isPageTurn("stay", queryClient, apiKeysFilter("b"))).toBe(false); // the organization switcher

    unsubscribe();
    expect(isPageTurn("stay", queryClient, apiKeysFilter("a"))).toBe(false); // Retry on the error page
  });
});

describe("frameData", () => {
  it("0003 RTE-4: stale data answers at once and refreshes behind the page", async () => {
    const queryClient = new QueryClient();
    const queryFn = vi.fn<() => Promise<string>>().mockResolvedValue("new");
    queryClient.setQueryData(apiKeysQuery("a").queryKey, "old" as never);

    await expect(frameData(queryClient, fetching(queryFn))).resolves.toBe("old");

    await expect.poll(() => queryClient.getQueryData(apiKeysQuery("a").queryKey)).toBe("new");
    expect(queryFn).toHaveBeenCalledOnce();
  });

  it("0017 AC-21: with nothing cached it waits, and a failure reaches the route", async () => {
    const queryFn = vi.fn<() => Promise<string>>().mockRejectedValue(new Error("down"));

    await expect(frameData(new QueryClient(), fetching(queryFn))).rejects.toThrow("down");
    expect(queryFn).toHaveBeenCalledOnce();
  });
});
