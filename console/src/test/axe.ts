import axe from "axe-core";
import { expect } from "vitest";

/** Spec 0003 A11Y-5: the WCAG tags every component test checks. */
const tags = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"];

/**
 * Spec 0003 A11Y-5: runs axe on what a test rendered and fails on any violation, listing each rule and the
 * elements it found.
 */
export async function expectNoAxeViolations(context: Element = document.body): Promise<void> {
  // A popover or drawer still fading in would be measured at part opacity, so let transitions settle first.
  await Promise.all(
    document.getAnimations().map((animation) => animation.finished.catch(() => undefined)),
  );
  const results = await axe.run(context, { runOnly: { type: "tag", values: tags } });
  const report = results.violations.map(
    (violation) =>
      `${violation.id}: ${violation.help}\n${violation.nodes.map((node) => `  ${node.target.join(" ")}`).join("\n")}`,
  );
  expect(report.join("\n\n")).toBe("");
}
