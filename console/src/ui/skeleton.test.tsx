import { useEffect, useState } from "react";
import { flushSync } from "react-dom";
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

// Spec 0015 AC-17, spec 0001 UX-21: nothing for the first 300 ms, then the skeleton for at least 500 ms. Real timers and
// a load the test ends, as spec 0003 QA-2 asks. A stall on a shared runner can make any timer late, so each assertion
// rests on the order things run in, and on lower bounds a late timer can't break. `patient` only waits for the page.

const patient = { timeout: 5000 };

/**
 * Records when the skeleton appears and disappears, for a load that takes `loadMs`, or without it, one that ends the
 * moment the skeleton is up, so the load ends inside the 500 ms in every engine and however a stall lands.
 */
function timeline(loadMs?: number) {
  const events: { shown: boolean; at: number }[] = [];
  const load = { startedAt: 0 };
  function Loader() {
    const [pending, setPending] = useState(true);
    // Above the hook, so it runs first: this timer starts before the hook's and is the shorter, and a browser runs
    // those in that order even when a stall leaves both due. `flushSync` commits in that same task, so the hook clears
    // its timer before it can fire. Without it the commit waits for a later task, and the hook's timer gets in first.
    useEffect(() => {
      load.startedAt = performance.now();
      if (loadMs === undefined) return undefined;
      const timer = setTimeout(() => {
        flushSync(() => {
          setPending(false);
        });
      }, loadMs);
      return () => {
        clearTimeout(timer);
      };
    }, []);
    const show = useDelayedPending(pending);
    useEffect(() => {
      events.push({ shown: show, at: performance.now() - load.startedAt });
      if (show && loadMs === undefined) {
        queueMicrotask(() => {
          setPending(false);
        });
      }
    }, [show]);
    return show ? <GridSkeleton /> : <p>{pending ? "waiting" : "loaded"}</p>;
  }
  return { Loader, events };
}

describe("useDelayedPending", () => {
  it("0015 AC-17: a load that ends before the delay never shows the skeleton", async () => {
    const { Loader, events } = timeline(250);
    const { screen } = await renderInRouter(<Loader />);

    await expect.element(screen.getByText("loaded"), patient).toBeVisible();
    // Long enough for the hook's timer to have fired, had the release not cleared it.
    await new Promise((resolve) => setTimeout(resolve, PENDING_DELAY_MS + 100));
    expect(events.some((event) => event.shown)).toBe(false);
  });

  it("0015 AC-17: a load that outlasts the delay shows the skeleton from 300 ms, for at least 500 ms", async () => {
    const { Loader, events } = timeline();
    const { screen } = await renderInRouter(<Loader />);

    await expect.element(screen.getByText("loaded"), patient).toBeVisible();
    // The text lands at the commit, and the effect that records the hide runs after it, later on a slow runner.
    await expect.poll(() => events.at(-1)?.shown, patient).toBe(false);
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
