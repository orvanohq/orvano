import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { activateLocale } from "@/shared/i18n";
import { renderInRouter } from "@/test/render";
import { DocsPane } from "./index";

// Spec 0015 AC-14, SHL-9: the pane's close callback and its copy, in three engines. The shell's own pane flows are
// in `src/gallery/power.test.tsx`.

afterEach(async () => {
  await activateLocale("en");
});

function Harness({ onClosed }: { onClosed: (generation: number) => void }) {
  const [isOpen, setOpen] = useState(true);
  return (
    <DocsPane
      isOpen={isOpen}
      onOpenChange={setOpen}
      isWide
      // A new function each render: the one from the render after closing is the one called.
      onClosed={() => {
        onClosed(isOpen ? 1 : 2);
      }}
    />
  );
}

describe("DocsPane", () => {
  it("0015 SHL-9: closing calls the latest onClosed once, a frame after the pane is gone (R-12)", async () => {
    const onClosed = vi.fn<(generation: number) => void>();
    const { screen } = await renderInRouter(<Harness onClosed={onClosed} />);

    await screen.getByRole("button", { name: "Close help" }).click();
    await expect
      .element(screen.getByRole("complementary", { name: "Help" }))
      .not.toBeInTheDocument();

    await expect.poll(() => onClosed.mock.calls).toEqual([[2]]);
  });

  it("0003 I18N-4: product names are placeholders, so the pseudo locale leaves them as they are (G-23)", async () => {
    await activateLocale("pseudo-LOCALE");
    const { screen } = await renderInRouter(
      <DocsPane isOpen onOpenChange={() => undefined} isWide onClosed={() => undefined} />,
    );
    const pane = screen.getByRole("complementary");

    await expect.element(pane).toBeVisible();
    const text = pane.element().textContent;
    expect(text).toContain("Orvano");
    expect(text).toContain("GitHub");
    // The sentence around them is still pseudo localized.
    expect(text).not.toContain("documentation is on");
  });
});
