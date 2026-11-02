import { useEffect, useRef, useState, type ReactNode } from "react";
import { useLingui } from "@lingui/react/macro";
import { cx } from "./cx";
import { VisuallyHidden } from "./visually-hidden";

/** Spec 0001 UX-21: how long loading must take before a skeleton shows, and how long it then stays. */
export const PENDING_DELAY_MS = 300;
export const PENDING_MINIMUM_MS = 500;

/**
 * Spec 0001 UX-21, spec 0015 AC-17: whether to show a skeleton for `isPending`. Nothing for the first 300 ms; once
 * shown, it stays at least 500 ms, so a fast response never flickers.
 */
export function useDelayedPending(isPending: boolean): boolean {
  const [shown, setShown] = useState(false);
  const shownAt = useRef(0);

  useEffect(() => {
    if (isPending && !shown) {
      const timer = setTimeout(() => {
        shownAt.current = performance.now();
        setShown(true);
      }, PENDING_DELAY_MS);
      return () => {
        clearTimeout(timer);
      };
    }
    if (!isPending && shown) {
      const remaining = shownAt.current + PENDING_MINIMUM_MS - performance.now();
      const timer = setTimeout(
        () => {
          setShown(false);
        },
        Math.max(0, remaining),
      );
      return () => {
        clearTimeout(timer);
      };
    }
    return undefined;
  }, [isPending, shown]);

  return shown;
}

const widths = {
  full: "w-full",
  "3/4": "w-3/4",
  "1/2": "w-1/2",
  "1/4": "w-1/4",
} as const;

const heights = {
  text: "h-4",
  title: "h-6",
  control: "h-control",
  block: "h-24",
} as const;

export interface SkeletonProps {
  width?: keyof typeof widths;
  height?: keyof typeof heights;
}

/** One placeholder block: `bg-muted`, a gentle pulse that stops under reduced motion (MOT-1). Decorative. */
export function Skeleton({ width = "full", height = "text" }: SkeletonProps) {
  return (
    <span
      aria-hidden="true"
      className={cx(
        "block animate-pulse rounded-sm bg-bg-muted motion-reduce:animate-none",
        widths[width],
        heights[height],
      )}
    />
  );
}

/**
 * The wrapper every skeleton layout shares, with "Loading" for screen readers. It isn't `aria-busy` itself: many
 * screen readers skip what's inside a busy element, which would hide that text. The caller marks the region being
 * loaded instead (`aria-busy` on the list, panel, or page section the skeleton stands in for).
 */
function Busy({ children }: { children: ReactNode }) {
  const { t } = useLingui();
  return (
    <div className="flex flex-col gap-3">
      <VisuallyHidden>{t`Loading`}</VisuallyHidden>
      {children}
    </div>
  );
}

export interface GridSkeletonProps {
  /** How many columns to draw (PRM-11: 4 by default). Always 8 rows. */
  columns?: number;
}

/** The skeleton of a grid or table: a header and 8 rows at the density's row height (UX-21, UX-32). Put it inside an `aria-busy` region. */
export function GridSkeleton({ columns = 4 }: GridSkeletonProps) {
  const cells = Array.from({ length: columns }, (_, index) => index);
  return (
    <Busy>
      <div aria-hidden="true" className="overflow-hidden rounded-md border">
        <div className="flex h-(--row-height) items-center gap-4 border-b bg-bg-muted px-3">
          {cells.map((cell) => (
            <span key={cell} className="h-3 flex-1 rounded-sm bg-bg-subtle" />
          ))}
        </div>
        {Array.from({ length: 8 }, (_, row) => (
          <div
            key={row}
            className="flex h-(--row-height) items-center gap-4 border-b px-3 last:border-b-0"
          >
            {cells.map((cell) => (
              <span
                key={cell}
                className="h-3 flex-1 animate-pulse rounded-sm bg-bg-muted motion-reduce:animate-none"
              />
            ))}
          </div>
        ))}
      </div>
    </Busy>
  );
}

/** The skeleton of a detail page or panel: a title line, a few lines of text, and a card. Put it inside an `aria-busy` region. */
export function ContentSkeleton() {
  return (
    <Busy>
      <Skeleton width="1/4" height="title" />
      <Skeleton width="3/4" />
      <Skeleton width="1/2" />
      <Skeleton height="block" />
    </Busy>
  );
}

/** The router's `defaultPendingComponent`: a page that's loading, inside the shell (spec 0003 RTE-3, RTE-4, UX-21). */
export function PageSkeleton() {
  return (
    <div aria-busy="true">
      <ContentSkeleton />
    </div>
  );
}

/**
 * The root route's pending component. A `beforeLoad` waiting on frame data (RTE-4, RTE-9) keeps every route pending,
 * the root too, so the shell can't render yet: this draws where its top bar and content will be.
 */
export function FrameSkeleton() {
  return (
    <div className="grid h-full grid-rows-[auto_minmax(0,1fr)] bg-bg">
      <div className="h-topbar border-b bg-bg-subtle" />
      <main className="min-w-0 overflow-auto px-4 py-6 lg:px-6">
        <PageSkeleton />
      </main>
    </div>
  );
}
