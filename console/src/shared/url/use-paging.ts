import { useLocation, useNavigate } from "@tanstack/react-router";
import { DEFAULT_PAGE_SIZE, type PageSearch } from "./paging";

/** The history entry's state: the cursors before this page, oldest first; `""` is the first page (spec 0014 PAG-2). */
type PagingState = { pageTrail?: readonly string[] };

/**
 * Spec 0001 UX-14, spec 0003 URL-3: moves a list between pages, each one a new history entry. The cursors seen so far
 * live in the history entry's state, so Back, Forward, and a reload keep Previous working. A pasted link has none, so
 * Previous goes to the first page. A view hook passes its search here.
 */
export function usePaging({ cursor, size }: PageSearch) {
  const trail =
    useLocation({ select: (location) => (location.state as PagingState).pageTrail }) ?? [];
  const navigate = useNavigate();

  function go(patch: PageSearch, pageTrail: readonly string[]) {
    void navigate({
      to: ".",
      search: (previous: object) => ({ ...previous, ...patch }),
      state: (previous: object) => ({ ...previous, pageTrail }),
    } as never);
  }

  return {
    pageSize: size ?? DEFAULT_PAGE_SIZE,
    /** Left out on the first page. */
    previous:
      cursor === undefined
        ? undefined
        : () => {
            go({ cursor: trail.at(-1) || undefined }, trail.slice(0, -1));
          },
    next: (nextCursor: string) => {
      go({ cursor: nextCursor }, [...trail, cursor ?? ""]);
    },
    /** UX-14: a new page size goes back to the first page. */
    setPageSize: (pageSize: number) => {
      go({ cursor: undefined, size: pageSize === DEFAULT_PAGE_SIZE ? undefined : pageSize }, []);
    },
  };
}
