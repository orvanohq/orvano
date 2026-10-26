import type { ReactNode } from "react";
import {
  Tab as AriaTab,
  TabList as AriaTabList,
  TabPanel as AriaTabPanel,
  Tabs as AriaTabs,
  type Key,
} from "react-aria-components";
import { cx, focusRing, focusRingInset } from "./cx";

export interface TabsProps {
  children: ReactNode;
  selectedKey?: Key;
  defaultSelectedKey?: Key;
  onSelectionChange?: (key: Key) => void;
  /**
   * Spec 0001 UX-5: panels that load content use manual activation (arrows move focus, Enter or Space selects).
   * Automatic selects on focus, for panels that render at once.
   */
  keyboardActivation?: "manual" | "automatic";
}

/**
 * Spec 0001 UX-5: real ARIA tabs (APG), only for switching views inside one panel, such as the SDK picker. Route
 * level tabs are `RouteTabs`. Keys: arrows (mirrored in right to left), Home, End.
 */
export function Tabs({ children, keyboardActivation = "manual", ...props }: TabsProps) {
  return (
    <AriaTabs {...props} keyboardActivation={keyboardActivation} className="flex flex-col gap-4">
      {children}
    </AriaTabs>
  );
}

export function TabList({
  "aria-label": label,
  children,
}: {
  "aria-label": string;
  children: ReactNode;
}) {
  return (
    <AriaTabList aria-label={label} className="flex overflow-x-auto border-b">
      {children}
    </AriaTabList>
  );
}

export function Tab({ id, children }: { id: string; children: ReactNode }) {
  return (
    <AriaTab
      id={id}
      className={cx(
        "-mb-px flex h-control-lg shrink-0 cursor-default items-center border-b-2 border-transparent px-3 font-medium whitespace-nowrap text-fg-muted data-hovered:text-fg data-selected:border-accent data-selected:text-fg",
        focusRingInset,
      )}
    >
      {children}
    </AriaTab>
  );
}

export function TabPanel({ id, children }: { id: string; children: ReactNode }) {
  return (
    <AriaTabPanel id={id} className={cx("rounded-md", focusRing)}>
      {children}
    </AriaTabPanel>
  );
}
