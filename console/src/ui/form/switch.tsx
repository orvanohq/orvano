import { useRef } from "react";
import type { MessageDescriptor } from "@lingui/core";
import { useLingui } from "@lingui/react";
import { SwitchButton, SwitchField, Text } from "react-aria-components";
import { useAriaAttribute } from "./field";

export interface SwitchProps {
  /** The visible label, after the track. */
  label: string;
  isSelected: boolean;
  onChange: (isSelected: boolean) => void;
  /**
   * Spec 0015 PRM-4 (UX-30): the caller may not change this setting. The switch stays focusable, ignores Space and
   * presses, and shows the reason as its description, linked by `aria-describedby`.
   */
  unavailableReason?: MessageDescriptor;
  /** Only for a switch that can't apply at all. Removes focus; prefer `unavailableReason`. */
  isDisabled?: boolean;
}

/**
 * design.md _Forms_: a setting that applies the moment it flips (not part of a form; forms use `Checkbox`). Space
 * toggles it. The track sits in a 24 px target.
 */
export function Switch({ label, unavailableReason, ...props }: SwitchProps) {
  const { i18n } = useLingui();
  const reason = unavailableReason ? i18n._(unavailableReason) : undefined;
  const inputRef = useRef<HTMLInputElement>(null);

  // PRM-4's wrapper: React Aria's read only state keeps focus and ignores Space and presses, and the input says it
  // is unavailable with `aria-disabled`, which React Aria's switch has no prop for.
  useAriaAttribute(inputRef, "aria-disabled", reason ? "true" : undefined);

  return (
    <SwitchField {...props} isReadOnly={reason !== undefined} inputRef={inputRef}>
      <SwitchButton className="group flex min-h-6 cursor-default items-center gap-2 data-readonly:cursor-not-allowed data-readonly:text-fg-muted">
        <span className="flex h-5 w-9 shrink-0 items-center rounded-full border border-border-strong bg-bg-muted p-0.5 transition-colors duration-(--duration-fast) ease-standard group-data-disabled:border-border group-data-focus-visible:outline-2 group-data-focus-visible:outline-offset-2 group-data-focus-visible:outline-solid group-data-focus-visible:outline-focus group-data-readonly:border-border group-data-selected:border-accent group-data-selected:bg-accent">
          <span className="size-3.5 rounded-full bg-fg-muted shadow-sm transition-transform duration-(--duration-fast) ease-standard group-data-selected:translate-x-4 group-data-selected:bg-accent-fg rtl:group-data-selected:-translate-x-4" />
        </span>
        {label}
      </SwitchButton>
      {reason ? (
        <Text slot="description" className="ps-11 text-sm text-fg-muted">
          {reason}
        </Text>
      ) : null}
    </SwitchField>
  );
}
