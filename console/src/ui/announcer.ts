import { announce as liveAnnounce } from "@react-aria/live-announcer";

/**
 * Spec 0015 AC-12: says a short status to screen readers through React Aria's one live announcer (polite). The
 * shell's switchers use it for their result count; `notify` and the `Announcer` (milestone 4) build on it.
 */
export function announce(message: string): void {
  liveAnnounce(message, "polite");
}
