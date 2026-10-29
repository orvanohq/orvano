import { useLingui } from "@lingui/react/macro";
import { Button as AriaButton } from "react-aria-components";
import { cx, focusRing } from "../cx";
import { Icon } from "../icon";
import { PendingIcon, SearchIcon } from "../icons";
import { Kbd } from "../kbd";

/** SHL-10: the palette's shortcut, everywhere but while a modal is open. */
export const PALETTE_KEYS = "Mod+K";

export interface PaletteButtonProps {
  onPress: () => void;
  /** From `useShortcut`: the Mod+K binding this button stands for (UX-26). */
  "aria-keyshortcuts": string;
  /** The palette's code is still loading after 300 ms (PRM-10): a spinner replaces the icon. */
  isPending?: boolean;
}

/**
 * Spec 0015 SHL-4: the visible control for Mod+K (every shortcut has one, UX-26). It shows the shortcut; below 64rem
 * only the icon shows, and the name is "Commands".
 */
export function PaletteButton({
  onPress,
  "aria-keyshortcuts": keyshortcuts,
  isPending,
}: PaletteButtonProps) {
  const { t } = useLingui();
  return (
    <AriaButton
      // React Aria's Button drops aria-keyshortcuts from its props, so it is set on the element.
      ref={(element) => {
        element?.setAttribute("aria-keyshortcuts", keyshortcuts);
      }}
      onPress={onPress}
      isPending={isPending}
      className={cx(
        "inline-flex h-control min-w-control cursor-default data-pending:cursor-wait items-center justify-center gap-2 rounded-md text-fg-muted data-hovered:bg-bg-muted data-hovered:text-fg data-pressed:bg-bg-muted lg:border lg:px-2",
        focusRing,
      )}
    >
      {isPending ? (
        <span className="animate-spin motion-reduce:animate-none">
          <Icon icon={PendingIcon} />
        </span>
      ) : (
        <Icon icon={SearchIcon} />
      )}
      {/* The name comes from the content, so it always holds the visible text (WCAG 2.5.3): "Commands ⌘K" from 64rem,
          and "Commands" below, where only the icon shows. */}
      <span className="sr-only lg:not-sr-only">{t`Commands`}</span>
      <span className="hidden lg:inline-flex">
        <Kbd keys={PALETTE_KEYS} />
      </span>
    </AriaButton>
  );
}
