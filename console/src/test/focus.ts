import { expect } from "vitest";

/**
 * Focuses an element directly and waits until it actually holds focus. Firefox and WebKit on CI can lag a frame, and
 * a settling component can re-render the element, so it retries until `document.activeElement` is the target. Pass
 * the element, or a locator (its element is read on every try).
 */
export async function focusElement(target: Element | { element: () => Element }): Promise<void> {
  await expect
    .poll(() => {
      const element = (target instanceof Element ? target : target.element()) as HTMLElement;
      element.focus();
      return document.activeElement === element;
    })
    .toBe(true);
}
