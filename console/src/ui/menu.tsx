import { useEffect, useId, useRef, type ReactNode } from "react";
import type { MessageDescriptor } from "@lingui/core";
import { useLingui } from "@lingui/react";
import {
  Header,
  Menu as AriaMenu,
  MenuItem as AriaMenuItem,
  MenuSection as AriaMenuSection,
  MenuTrigger as AriaMenuTrigger,
  Separator,
  Text,
  type Key,
  type Selection,
} from "react-aria-components";
import { cx, focusRingInset } from "./cx";
import { Icon } from "./icon";
import { CheckIcon, type IconComponent } from "./icons";
import { Popover, type PopoverProps } from "./popover";

export interface MenuTriggerProps {
  /** A design system button (`Button`, `IconButton`) or a React Aria `Button`. */
  trigger: ReactNode;
  children: ReactNode;
  placement?: PopoverProps["placement"];
  /** Content above the menu that isn't an item (the account menu's name and email). */
  header?: ReactNode;
}

/** design.md _Overlays_: a button that opens a `Menu` in a `Popover`. Escape closes and returns focus. */
export function MenuTrigger({ trigger, children, placement, header }: MenuTriggerProps) {
  return (
    <AriaMenuTrigger>
      {trigger}
      <Popover placement={placement}>
        {header}
        {children}
      </Popover>
    </AriaMenuTrigger>
  );
}

export interface MenuProps {
  "aria-label": string;
  children: ReactNode;
  onAction?: (key: Key) => void;
  /** Shown instead of the items when there are none (the command palette's "No matching actions"). */
  emptyMessage?: string;
}

/** APG menu: arrows, Enter, Escape, and type ahead (React Aria). */
export function Menu({ children, emptyMessage, ...props }: MenuProps) {
  const ref = useRef<HTMLDivElement>(null);
  // PRM-4: React Aria's MenuItem doesn't pass `aria-disabled` through, and its `isDisabled` would remove focus, so
  // unavailable items (marked `data-unavailable`) get it here. The items render after the menu (React Aria builds
  // its collection first), so an observer marks them as they appear. React never manages the attribute.
  useEffect(() => {
    const menu = ref.current;
    if (!menu) return;
    const mark = () => {
      for (const item of menu.querySelectorAll("[data-unavailable]:not([aria-disabled])")) {
        item.setAttribute("aria-disabled", "true");
      }
    };
    mark();
    const observer = new MutationObserver(mark);
    observer.observe(menu, { childList: true, subtree: true });
    return () => {
      observer.disconnect();
    };
  }, []);
  return (
    <AriaMenu
      {...props}
      ref={ref}
      renderEmptyState={
        emptyMessage ? () => <p className="px-2 py-3 text-fg-muted">{emptyMessage}</p> : undefined
      }
      className="max-h-[inherit] min-w-48 overflow-y-auto p-1 outline-none"
    >
      {children}
    </AriaMenu>
  );
}

export interface MenuItemProps {
  id: string;
  children: ReactNode;
  onAction?: () => void;
  icon?: IconComponent;
  tone?: "neutral" | "danger";
  /**
   * Spec 0015 PRM-4: the caller may not take this action. The item stays focusable, ignores Enter and presses, and
   * shows the reason as its description line.
   */
  unavailableReason?: MessageDescriptor;
  /** Type ahead text, needed when `children` is not a plain string. */
  textValue?: string;
}

const itemClass = cx(
  "group flex min-h-control cursor-default items-center gap-2 rounded-md px-2 py-1 text-fg data-focused:bg-bg-muted data-unavailable:text-fg-muted",
  focusRingInset,
);

export function MenuItem({
  id,
  children,
  onAction,
  icon,
  tone = "neutral",
  unavailableReason,
  textValue,
}: MenuItemProps) {
  const { i18n } = useLingui();
  const reasonId = useId();
  const reason = unavailableReason ? i18n._(unavailableReason) : undefined;
  return (
    <AriaMenuItem
      id={id}
      data-unavailable={reason ? true : undefined}
      textValue={textValue ?? (typeof children === "string" ? children : undefined)}
      onAction={reason ? undefined : onAction}
      className={cx(itemClass, tone === "danger" && "text-danger-text")}
    >
      {({ selectionMode, isSelected }) => (
        <>
          {selectionMode !== "none" ? (
            <span className="flex size-4 shrink-0 items-center justify-center">
              {isSelected && <Icon icon={CheckIcon} />}
            </span>
          ) : (
            icon && <Icon icon={icon} />
          )}
          <span className="flex min-w-0 flex-col">
            <Text slot="label" className="truncate">
              {children}
            </Text>
            {reason && (
              <Text slot="description" id={reasonId} className="text-sm text-fg-muted">
                {reason}
              </Text>
            )}
          </span>
        </>
      )}
    </AriaMenuItem>
  );
}

export interface MenuSectionProps {
  title: string;
  children: ReactNode;
  /** A single selection group (Theme, Density): the checked item shows a check mark. */
  selectedKey?: string;
  onSelect?: (key: string) => void;
}

export function MenuSection({ title, children, selectedKey, onSelect }: MenuSectionProps) {
  const selectable = selectedKey !== undefined;
  return (
    <AriaMenuSection
      selectionMode={selectable ? "single" : undefined}
      disallowEmptySelection={selectable || undefined}
      selectedKeys={selectable ? [selectedKey] : undefined}
      onSelectionChange={
        selectable
          ? (keys: Selection) => {
              if (keys === "all") return;
              const [key] = keys;
              if (key !== undefined) onSelect?.(String(key));
            }
          : undefined
      }
      className="flex flex-col"
    >
      <Header className="px-2 pt-2 pb-1 text-xs font-semibold text-fg-muted">{title}</Header>
      {children}
    </AriaMenuSection>
  );
}

export function MenuSeparator() {
  return <Separator className="my-1 border-t" />;
}
