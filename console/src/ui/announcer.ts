import { useEffect } from "react";
import { announce as liveAnnounce, destroyAnnouncer } from "@react-aria/live-announcer";

/**
 * Spec 0001 UX-41, spec 0003 A11Y-4: says a short status to screen readers ("Copied", "3 projects") through React
 * Aria's one live announcer, the region React Aria's own widgets announce through, so nothing is read twice. Polite
 * by default; assertive only for something that must interrupt.
 */
export function announce(message: string, politeness: "polite" | "assertive" = "polite"): void {
  liveAnnounce(message, politeness);
}

/**
 * Spec 0015 PRM-1: the shell's announcer. Mount it once at the root: it puts the live regions in the page up front, so
 * the first announcement isn't lost while a screen reader is still noticing a region that just appeared.
 */
export function Announcer() {
  useEffect(() => {
    liveAnnounce("", "polite");
  }, []);
  return null;
}

/**
 * Test only: removes the announcer's live regions. A pending button announces itself by `aria-labelledby`, and once
 * the button is gone that leftover node would fail the next test's axe run.
 */
export function resetAnnouncerForTests(): void {
  destroyAnnouncer();
}
