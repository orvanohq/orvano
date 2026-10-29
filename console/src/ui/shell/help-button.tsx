import { useLingui } from "@lingui/react/macro";
import type { Ref } from "react";
import { IconButton } from "../button";
import { HelpIcon } from "../icons";

export interface HelpButtonProps {
  /** Whether the docs pane is open; the button says so with `aria-expanded`. */
  isExpanded: boolean;
  onPress: () => void;
  /** The button, which the shell focuses again when the pane closes (SHL-9). */
  ref?: Ref<HTMLButtonElement>;
  /** The docs pane's code is still loading after 300 ms (PRM-10). */
  isPending?: boolean;
}

/**
 * Spec 0015 SHL-4, spec 0001 UX-31 (WCAG 3.2.6): the help button, in the same place in the top bar in every scope. It
 * opens and closes the docs pane.
 */
export function HelpButton({ isExpanded, onPress, ref, isPending }: HelpButtonProps) {
  const { t } = useLingui();
  return (
    <IconButton
      ref={ref}
      icon={HelpIcon}
      aria-label={t`Help`}
      aria-expanded={isExpanded}
      isPending={isPending}
      onPress={onPress}
    />
  );
}
