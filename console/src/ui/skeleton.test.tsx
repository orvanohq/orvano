import { useEffect, useState } from "react";
import { describe, expect, it } from "vitest";
import { expectNoAxeViolations } from "@/test/axe";
import { renderInRouter } from "@/test/render";
import {
  ContentSkeleton,
  GridSkeleton,
  PENDING_DELAY_MS,
  PENDING_MINIMUM_MS,
  useDelayedPending,
} from "./skeleton";

// Spec 0015 AC-17, spec 0001 UX-21: nothing for the first 300 ms, then the skeleton for at least 500 ms. Real timers,
// as the spec's timing scenario asks.

/** Records when the skeleton appears and disappears, for a load that takes `loadMs`. */
function timeline(loadMs: number) {
  const events: { shown: boolean; at: number }[] = [];
  const start = performance.now();
  function Loader() {
    const [pending, setPending] = useState(true);
    const show = useDelayedPending(pending);
    useEffect(() => {
      const timer = setTimeout(() => {
        setPending(false);
      }, loadMs);
      return () => {
        clearTimeout(timer);
      };
    }, []);
    useEffect(() => {
      events.push({ shown: show, at: performance.now() - start });
    }, [show]);
    return show ? <GridSkeleton /> : <p>{pending ? "waiting" : "loaded"}</p>;
  }
  return { Loader, events };
}

describe("useDelayedPending", () => {
  // 100 ms, well before the 300 ms delay: a slow engine or GC stall on a shared runner can't push it past.
  it("0015 AC-17: a load that ends before the delay never shows the skeleton", async () => {
    const { Loader, events } = timeline(100);
    const { screen } = await renderInRouter(<Loader />);

    await expect.element(screen.getByText("loaded")).toBeVisible();
    await new Promise((resolve) => setTimeout(resolve, 400));
    expect(events.some((event) => event.shown)).toBe(false);
  });

  it("0015 AC-17: a load that ends after 400 ms shows the skeleton from 300 ms, for at least 500 ms", async () => {
    const { Loader, events } = timeline(400);
    const { screen } = await renderInRouter(<Loader />);

    await expect.element(screen.getByText("loaded"), { timeout: 3000 }).toBeVisible();
    const shown = events.find((event) => event.shown);
    const hidden = events.findLast((event) => !event.shown);
    expect(shown?.at).toBeGreaterThanOrEqual(PENDING_DELAY_MS);
    // It appears no earlier than 300 ms and then stays at least 500 ms, so it can't be gone before 800 ms. Both are
    // measured from the start: a gap between two effect times would shrink when a slow engine delays the first.
    expect(hidden?.at).toBeGreaterThanOrEqual(PENDING_DELAY_MS + PENDING_MINIMUM_MS);
  });
});

describe("GridSkeleton and ContentSkeleton", () => {
  it("0015 AC-17: the grid skeleton draws 8 rows of the given columns, named Loading inside the busy region", async () => {
    const { screen } = await renderInRouter(
      <div>
        <section aria-busy="true" aria-label="Keys">
          <GridSkeleton columns={3} />
        </section>
        <ContentSkeleton />
      </div>,
    );

    // The skeleton never marks itself busy: that would hide its own "Loading" text from many screen readers.
    const busy = document.querySelectorAll("[aria-busy]");
    expect(busy).toHaveLength(1);
    const grid = busy[0]?.querySelector("[aria-hidden='true']");
    // A header row plus 8 rows, each with 3 cells.
    expect(grid?.children).toHaveLength(9);
    expect(grid?.children[1]?.children).toHaveLength(3);
    await expect.element(screen.getByText("Loading").first()).toBeInTheDocument();
    expect(screen.getByText("Loading").all()).toHaveLength(2);
    await expectNoAxeViolations();
  });
});
