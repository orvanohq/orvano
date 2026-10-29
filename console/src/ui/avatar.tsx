import { useLingui } from "@lingui/react";
import { cx } from "./cx";

export interface AvatarProps {
  name: string;
  email: string;
  size?: "sm" | "md";
}

let graphemes: Intl.Segmenter | undefined;

/** The first user perceived character: a letter with its combining marks, or a whole joined emoji. */
function first(text: string | undefined): string {
  if (!text) return "";
  graphemes ??= new Intl.Segmenter(undefined, { granularity: "grapheme" });
  return graphemes.segment(text)[Symbol.iterator]().next().value?.segment ?? "";
}

/**
 * Spec 0015 PRM-11: up to two initials, the first graphemes of the first and last words of the name, uppercased for
 * the active locale; else the first letter of the email.
 */
export function initials(name: string, email: string, locale: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  const letters =
    words.length === 0
      ? first(email.trim())
      : words.length === 1
        ? first(words[0])
        : first(words[0]) + first(words.at(-1));
  return letters.toLocaleUpperCase(locale);
}

/**
 * Decorative (`aria-hidden`): its button carries the name ("Account"). The initials are drawn by CSS from
 * `data-initials`, not as text, so the button's visible text never contradicts its name (WCAG 2.5.3).
 */
export function Avatar({ name, email, size = "md" }: AvatarProps) {
  const { i18n } = useLingui();
  return (
    <span
      aria-hidden="true"
      data-initials={initials(name, email, i18n.locale)}
      className={cx(
        "inline-flex shrink-0 items-center justify-center rounded-full bg-accent-subtle font-semibold text-accent-subtle-fg select-none before:content-[attr(data-initials)]",
        size === "sm" ? "size-6 text-xs" : "size-8 text-sm",
      )}
    />
  );
}
