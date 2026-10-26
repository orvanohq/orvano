import type { ReactElement, ReactNode } from "react";
import { Tooltip as AriaTooltip, TooltipTrigger } from "react-aria-components";

export interface TooltipProps {
  /** The tooltip text. Never essential information (UX-40): the trigger has its own accessible name. */
  content: ReactNode;
  /** A focusable design system control (`Button`, `IconButton`, `Link`). */
  children: ReactElement;
  placement?: "top" | "bottom" | "start" | "end";
}

/** Opens on hover and keyboard focus, stays while hovered, closes on Escape (design.md _Foundations_). */
export function Tooltip({ content, children, placement = "top" }: TooltipProps) {
  return (
    <TooltipTrigger delay={500} closeDelay={150}>
      {children}
      <AriaTooltip
        placement={placement}
        offset={6}
        className="z-(--z-tooltip) max-w-64 rounded-sm bg-tooltip px-2 py-1 text-xs text-tooltip-fg shadow-sm"
      >
        {content}
      </AriaTooltip>
    </TooltipTrigger>
  );
}
