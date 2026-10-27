import type { ReactNode } from "react";
import { Dialog, Modal, ModalOverlay } from "react-aria-components";
import { cx } from "../cx";

export interface DrawerProps {
  isOpen: boolean;
  onOpenChange: (isOpen: boolean) => void;
  /** Names the drawer's content ("Project", "Tables"). */
  "aria-label": string;
  /** The primary sidebar's width, the resource list's, or the full width (the narrow docs pane). */
  width?: "sidebar" | "resource-list" | "full";
  /** Sidebars open from the inline start; the docs pane from the inline end. */
  side?: "start" | "end";
  children: ReactNode;
}

const widths = {
  sidebar: cx("w-sidebar"),
  "resource-list": cx("w-resource-list"),
  full: cx("w-full"),
} as const;

// MOT-1: translation is physical, so each slide pairs translate-x with its rtl: mirror. Reduced motion zeroes the
// duration.
const sides = {
  start: cx(
    "start-0 border-e data-entering:-translate-x-full data-exiting:-translate-x-full rtl:data-entering:translate-x-full rtl:data-exiting:translate-x-full",
  ),
  end: cx(
    "end-0 border-s data-entering:translate-x-full data-exiting:translate-x-full rtl:data-entering:-translate-x-full rtl:data-exiting:-translate-x-full",
  ),
} as const;

/**
 * Spec 0015 SHL-6: a modal React Aria `Modal` sliding from the inline start (or end), labeled by its content. It
 * traps focus, closes with Escape or a press outside, and returns focus to the button that opened it.
 */
export function Drawer({
  isOpen,
  onOpenChange,
  "aria-label": label,
  width = "sidebar",
  side = "start",
  children,
}: DrawerProps) {
  return (
    <ModalOverlay
      data-modal // SHL-10: shell shortcuts do nothing while a modal is open
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      isDismissable
      className="fixed inset-0 z-(--z-drawer) bg-scrim transition-opacity duration-(--duration-slow) ease-standard data-entering:opacity-0 data-exiting:opacity-0"
    >
      <Modal
        className={cx(
          "fixed inset-y-0 flex max-w-full flex-col bg-bg-subtle shadow-lg transition-transform duration-(--duration-slow) ease-standard",
          widths[width],
          sides[side],
        )}
      >
        <Dialog aria-label={label} className="flex min-h-0 flex-1 flex-col outline-none">
          {children}
        </Dialog>
      </Modal>
    </ModalOverlay>
  );
}
