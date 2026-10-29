import { useEffect, useEffectEvent } from "react";
import type { MessageDescriptor } from "@lingui/core";
import { registerShortcut } from "@/shared/stores/shortcuts";
import { ariaKeyshortcuts } from "./kbd";
import { isApplePlatform } from "./platform";

export interface UseShortcutOptions {
  /** Stable id in the registry ("command-palette"). */
  id: string;
  /**
   * `Mod+K` (a shortcut that works everywhere uses a modifier, UX-26), or one typed character such as `?`, matched on
   * `event.key` and ignored while typing in a field.
   */
  keys: string;
  /** What it does, as listed by `ShortcutHelp`. */
  label: MessageDescriptor;
  onTrigger: () => void;
  /** Off while the shortcut shouldn't apply (the action is already open). Stays listed in the registry. */
  isEnabled?: boolean;
}

/**
 * Spec 0015 SHL-10: whether a modal (dialog, sheet, drawer, the palette) is open. Each design system modal overlay
 * carries `data-modal`; the shell behind it is inert, so its shortcuts do nothing.
 */
export function isModalOpen(): boolean {
  return document.querySelector("[data-modal]") !== null;
}

function isTyping(target: EventTarget | null): boolean {
  if (!(target instanceof HTMLElement)) return false;
  return (
    target.isContentEditable ||
    target instanceof HTMLTextAreaElement ||
    target instanceof HTMLSelectElement ||
    (target instanceof HTMLInputElement &&
      !["button", "checkbox", "radio", "range", "reset", "submit"].includes(target.type))
  );
}

/** Whether a keydown is the shortcut `keys` (PRM-11: Mod is Meta on Apple platforms, Control elsewhere). */
export function matchesShortcut(event: KeyboardEvent, keys: string): boolean {
  const parts = keys === "+" ? ["+"] : keys.split("+");
  const key = parts.at(-1) ?? "";
  const modifiers = new Set(parts.slice(0, -1));
  if (modifiers.size === 0) {
    // A typed character: `?` is Shift+/ on most layouts, so only the character counts.
    return event.key === key && !event.ctrlKey && !event.metaKey && !event.altKey;
  }
  const apple = isApplePlatform();
  const mod = modifiers.has("Mod");
  return (
    matchesKey(event, key) &&
    event.metaKey === (mod && apple) &&
    event.ctrlKey === (mod && !apple) &&
    event.altKey === modifiers.has("Alt") &&
    event.shiftKey === modifiers.has("Shift")
  );
}

/**
 * The key part of a modifier shortcut. A letter matches the typed letter, or, when the layout types no Latin letter
 * (Cyrillic, Greek), the physical key (`event.code`): Mod+K is then the key where K sits. A layout that types a
 * Latin letter (AZERTY, Dvorak) keeps matching on the letter, so the shortcut follows what the key says.
 */
function matchesKey(event: KeyboardEvent, key: string): boolean {
  if (event.key.toLowerCase() === key.toLowerCase()) return true;
  return (
    /^[a-z]$/i.test(key) && !/^[a-z]$/i.test(event.key) && event.code === `Key${key.toUpperCase()}`
  );
}

/**
 * Spec 0003 A11Y-7, spec 0001 UX-26: the only way to bind a keyboard shortcut. It adds the shortcut to the registry
 * `ShortcutHelp` lists, listens on the document, and returns the `aria-keyshortcuts` prop for the visible control that
 * does the same thing (every shortcut has one). Nothing fires while a modal is open, and a single character shortcut
 * never fires while you type in a field.
 */
export function useShortcut({ id, keys, label, onTrigger, isEnabled = true }: UseShortcutOptions) {
  const trigger = useEffectEvent(onTrigger);

  useEffect(() => registerShortcut({ id, keys, label }), [id, keys, label]);

  useEffect(() => {
    if (!isEnabled) return;
    const onKeyDown = (event: KeyboardEvent) => {
      // A held key repeats its keydown; the shortcut fires once per press.
      if (event.defaultPrevented || event.isComposing || event.repeat) return;
      if (!matchesShortcut(event, keys)) return;
      if (!keys.includes("+") && isTyping(event.target)) return;
      if (isModalOpen()) return;
      event.preventDefault();
      trigger();
    };
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [keys, isEnabled]);

  return { "aria-keyshortcuts": ariaKeyshortcuts(keys) };
}
