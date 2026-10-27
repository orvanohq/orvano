import { useLingui } from "@lingui/react/macro";
import { isApplePlatform } from "./platform";

// Spec 0015 PRM-11: a shortcut is written `Mod+K` (Mod is Meta on Apple platforms, Control elsewhere) or as one typed
// character (`?`). These helpers turn it into what `Kbd` shows and what `aria-keyshortcuts` says.

// The ARIA names of Mod (aria-keyshortcuts uses the DOM key names, never translated).
const APPLE_MOD = "Meta";
const OTHER_MOD = "Control";

function parts(keys: string): string[] {
  // A lone `+` is a key, not a separator.
  return keys === "+" ? ["+"] : keys.split("+");
}

/** The `aria-keyshortcuts` value: `Meta+K` or `Control+K`, and a typed character as itself. */
export function ariaKeyshortcuts(keys: string): string {
  const apple = isApplePlatform();
  return parts(keys)
    .map((part) => (part === "Mod" ? (apple ? APPLE_MOD : OTHER_MOD) : part))
    .join("+");
}

/** The translated names of the modifier keys outside Apple platforms ("Ctrl", or "Strg" in German). */
export interface ModifierNames {
  ctrl: string;
  shift: string;
  alt: string;
}

/** The keys as shown: `⌘` and the key on Apple platforms, `Ctrl` and the key elsewhere. */
export function displayKeys(keys: string, names: ModifierNames): string[] {
  const apple = isApplePlatform();
  return parts(keys).map((part) => {
    if (part === "Mod") return apple ? "⌘" : names.ctrl;
    if (part === "Shift") return apple ? "⇧" : names.shift;
    if (part === "Alt") return apple ? "⌥" : names.alt;
    return part;
  });
}

export interface KbdProps {
  /** `Mod+K`, or one typed character such as `?`. */
  keys: string;
}

/**
 * design.md _Foundations_: a keyboard shortcut as `kbd` elements, one per key inside an outer `kbd` (the HTML
 * pattern for a key combination). `radius-sm`, `bg-muted`, `text-xs`.
 */
export function Kbd({ keys }: KbdProps) {
  const { t } = useLingui();
  const names = {
    ctrl: t({ message: "Ctrl", context: "keyboard key" }),
    shift: t({ message: "Shift", context: "keyboard key" }),
    alt: t({ message: "Alt", context: "keyboard key" }),
  };
  return (
    <kbd className="inline-flex items-center gap-0.5 font-sans">
      {displayKeys(keys, names).map((key) => (
        <kbd
          // A key appears once in a shortcut, so it is its own identity.
          key={key}
          className="inline-flex h-5 min-w-5 items-center justify-center rounded-sm border bg-bg-muted px-1 text-xs text-fg-muted"
        >
          {key}
        </kbd>
      ))}
    </kbd>
  );
}
