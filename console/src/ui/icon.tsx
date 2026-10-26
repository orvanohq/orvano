import type { IconComponent } from "./icons";
import { cx } from "./cx";

export interface IconProps {
  icon: IconComponent;
  /** 16 px in controls and nav, 20 px in empty states and banners, never above 24 px (ICN-2). */
  size?: 16 | 20 | 24;
  /** Makes the icon meaningful: it gets `role="img"` and this name. Decorative (`aria-hidden`) without it. */
  label?: string;
  /** Mirrors direction icons (chevrons, arrows, back) under `dir="rtl"`. */
  flipInRtl?: boolean;
}

/** Spec 0015 ICN-2: a Lucide icon at a token size, `currentColor`, stroke 1.75. */
export function Icon({ icon: Glyph, size = 16, label, flipInRtl = false }: IconProps) {
  return (
    <Glyph
      size={size}
      strokeWidth={1.75}
      color="currentColor"
      className={cx("shrink-0", flipInRtl && "rtl:-scale-x-100")}
      {...(label ? { role: "img", "aria-label": label } : { "aria-hidden": true })}
    />
  );
}
