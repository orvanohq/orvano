import type { ReactNode } from "react";
import { VisuallyHidden as AriaVisuallyHidden } from "react-aria";

/** Text for screen readers only. */
export function VisuallyHidden({ id, children }: { id?: string; children: ReactNode }) {
  return <AriaVisuallyHidden id={id}>{children}</AriaVisuallyHidden>;
}
