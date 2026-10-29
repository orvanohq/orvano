import { useId, type ReactNode, type Ref } from "react";
import type { MessageDescriptor } from "@lingui/core";
import { useLingui } from "@lingui/react";
import { Button as AriaButton, type PressEvent } from "react-aria-components";
import { cx, focusRing } from "./cx";
import { Icon } from "./icon";
import { PendingIcon, type IconComponent } from "./icons";
import { Tooltip } from "./tooltip";
import { VisuallyHidden } from "./visually-hidden";

export type ButtonVariant = "primary" | "secondary" | "ghost" | "danger";
export type ButtonSize = "sm" | "md" | "lg";

interface BaseProps {
  onPress?: (event: PressEvent) => void;
  type?: "button" | "submit" | "reset";
  /** The id of the `<form>` a submit button submits, when it sits outside it (a sheet's footer). */
  form?: string;
  /** For controls that truly can't act (a submit while pending). Removes focus; prefer `unavailableReason`. */
  isDisabled?: boolean;
  /**
   * Spec 0015 PRM-4 (UX-30): the caller may not take this action. The button stays focusable, ignores presses and
   * Enter, and exposes the reason through `aria-describedby` and a tooltip.
   */
  unavailableReason?: MessageDescriptor;
  /** Takes focus when it mounts: the safe choice in a dialog (Cancel in a tier 2 delete, UX-23). */
  initialFocus?: boolean;
  /** `close` closes the `Dialog` or `SideSheet` it sits in (a sheet asks first when it has unsaved changes). */
  slot?: "close";
  /** For a button that shows and hides a region beside it (the help button and the docs pane). */
  "aria-expanded"?: boolean;
  /** The button element, for a caller that returns focus to it (the help button, SHL-9). */
  ref?: Ref<HTMLButtonElement>;
}

export interface ButtonProps extends BaseProps {
  children: ReactNode;
  variant?: ButtonVariant;
  size?: ButtonSize;
  /**
   * Busy (FRM-4, UX-8): shows a spinner, stays focusable, ignores presses, and React Aria announces the busy state.
   */
  isPending?: boolean;
}

export interface IconButtonProps extends BaseProps {
  icon: IconComponent;
  /** Required: an icon only button has no visible label (A11Y-3). The tooltip shows the same text. */
  "aria-label": string;
  variant?: "ghost" | "secondary";
  size?: "sm" | "md";
  flipInRtl?: boolean;
  /** Busy: a spinner replaces the icon, it stays focusable, ignores presses, and React Aria announces it. */
  isPending?: boolean;
}

// design.md _Button recipes_. Hover and press styles skip unavailable buttons, which use the disabled look. Filled
// variants carry a transparent border, which forced colors mode paints, so they keep a boundary there (THM-6).
const variants: Record<ButtonVariant, string> = {
  primary: cx(
    "border border-transparent bg-accent text-accent-fg not-aria-disabled:data-hovered:bg-accent-hover not-aria-disabled:data-pressed:bg-accent-hover",
  ),
  secondary: cx(
    "border border-border-strong bg-bg text-fg not-aria-disabled:data-hovered:bg-bg-muted not-aria-disabled:data-pressed:bg-bg-muted",
  ),
  ghost: cx(
    "bg-transparent text-fg not-aria-disabled:data-hovered:bg-bg-muted not-aria-disabled:data-pressed:bg-bg-muted",
  ),
  danger: cx(
    "border border-transparent bg-danger text-danger-fg not-aria-disabled:data-hovered:bg-danger-hover not-aria-disabled:data-pressed:bg-danger-hover",
  ),
};

const unavailable: Record<ButtonVariant, string> = {
  primary: cx(
    "aria-disabled:border aria-disabled:border-border aria-disabled:bg-bg-muted aria-disabled:text-fg-muted",
  ),
  secondary: cx(
    "aria-disabled:border-border aria-disabled:bg-bg-muted aria-disabled:text-fg-muted",
  ),
  ghost: cx("aria-disabled:text-fg-muted"),
  danger: cx(
    "aria-disabled:border aria-disabled:border-border aria-disabled:bg-bg-muted aria-disabled:text-fg-muted",
  ),
};

const disabled: Record<ButtonVariant, string> = {
  primary: cx(
    "data-disabled:border data-disabled:border-border data-disabled:bg-bg-muted data-disabled:text-fg-muted",
  ),
  secondary: cx(
    "data-disabled:border-border data-disabled:bg-bg-muted data-disabled:text-fg-muted",
  ),
  ghost: cx("data-disabled:text-fg-muted"),
  danger: cx(
    "data-disabled:border data-disabled:border-border data-disabled:bg-bg-muted data-disabled:text-fg-muted",
  ),
};

const sizes: Record<ButtonSize, string> = {
  sm: cx("h-control-sm gap-1.5 px-2 text-sm"),
  md: cx("h-control gap-2 px-3 text-base"),
  lg: cx("h-control-lg gap-2 px-3 text-base"),
};

const iconSizes: Record<"sm" | "md", string> = {
  sm: cx("size-control-sm"),
  md: cx("size-control"),
};

const base = cx(
  "inline-flex shrink-0 cursor-default items-center justify-center rounded-md font-semibold whitespace-nowrap select-none aria-disabled:cursor-not-allowed",
);

/** The shared press handling: an unavailable button keeps focus and its name but does nothing (PRM-4). */
function PressableButton({
  className,
  label,
  children,
  onPress,
  type = "button",
  form,
  isDisabled,
  isPending,
  initialFocus,
  slot,
  unavailableReason,
  "aria-expanded": expanded,
  ref,
}: BaseProps & { className: string; label?: string; isPending?: boolean; children: ReactNode }) {
  const { i18n } = useLingui();
  const reasonId = useId();
  const reason = unavailableReason ? i18n._(unavailableReason) : undefined;

  const button = (
    <AriaButton
      ref={ref}
      className={className}
      aria-label={label}
      // A submit button that is unavailable must not submit its form either.
      type={reason ? "button" : type}
      form={form}
      isDisabled={isDisabled}
      isPending={isPending}
      // oxlint-disable-next-line jsx-a11y/no-autofocus -- UX-23 and the APG alert dialog: focus starts on the safe action
      autoFocus={initialFocus}
      slot={slot}
      aria-expanded={expanded}
      aria-disabled={reason ? true : undefined}
      aria-describedby={reason ? reasonId : undefined}
      onPress={reason ? undefined : onPress}
    >
      {children}
    </AriaButton>
  );

  if (reason) {
    return (
      <>
        <Tooltip content={reason}>{button}</Tooltip>
        <VisuallyHidden id={reasonId}>{reason}</VisuallyHidden>
      </>
    );
  }
  if (label) return <Tooltip content={label}>{button}</Tooltip>;
  return button;
}

/** design.md _Button recipes_. One `primary` per surface, at the inline end (UX-36). */
export function Button({
  children,
  variant = "secondary",
  size = "md",
  isPending,
  ...props
}: ButtonProps) {
  return (
    <PressableButton
      {...props}
      isPending={isPending}
      className={cx(
        base,
        focusRing,
        sizes[size],
        variants[variant],
        unavailable[variant],
        disabled[variant],
        "data-pending:cursor-wait",
      )}
    >
      {isPending ? (
        <span className="animate-spin motion-reduce:animate-none">
          <Icon icon={PendingIcon} />
        </span>
      ) : null}
      {children}
    </PressableButton>
  );
}

/** An icon only button. Its `aria-label` is required by type, and shows as its tooltip. */
export function IconButton({
  icon,
  "aria-label": label,
  variant = "ghost",
  size = "md",
  flipInRtl,
  isPending,
  ...props
}: IconButtonProps) {
  return (
    <PressableButton
      {...props}
      label={label}
      isPending={isPending}
      className={cx(
        base,
        focusRing,
        iconSizes[size],
        variants[variant],
        unavailable[variant],
        disabled[variant],
        "data-pending:cursor-wait",
      )}
    >
      {isPending ? (
        <span className="animate-spin motion-reduce:animate-none">
          <Icon icon={PendingIcon} />
        </span>
      ) : (
        <Icon icon={icon} flipInRtl={flipInRtl} />
      )}
    </PressableButton>
  );
}
