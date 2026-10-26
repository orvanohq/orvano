import type { ReactNode, RefObject } from "react";
import {
  Popover as AriaPopover,
  type PopoverProps as AriaPopoverProps,
} from "react-aria-components";

export interface PopoverProps {
  children: ReactNode;
  placement?: AriaPopoverProps["placement"];
  /** Controlled use, for a popover opened by something other than its trigger. */
  isOpen?: boolean;
  onOpenChange?: (isOpen: boolean) => void;
  triggerRef?: RefObject<HTMLElement | null>;
}

/**
 * design.md _Overlays_: `bg-raised` with a border and `shadow-sm`. It fades and scales from 98% (MOT-1; reduced
 * motion makes it instant). Escape closes it and focus returns to its trigger.
 */
export function Popover({ children, placement = "bottom start", ...props }: PopoverProps) {
  return (
    <AriaPopover
      {...props}
      placement={placement}
      offset={4}
      className="z-(--z-popover) max-w-[calc(100vw-2rem)] origin-top overflow-hidden rounded-md border bg-bg-raised text-fg shadow-sm transition duration-(--duration-base) ease-standard data-entering:scale-98 data-entering:opacity-0 data-exiting:opacity-0"
    >
      {children}
    </AriaPopover>
  );
}
