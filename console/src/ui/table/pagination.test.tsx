import { useState } from "react";
import { describe, expect, it, vi } from "vitest";
import { page } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { renderInRouter } from "@/test/render";
import { Pagination } from ".";

function Paged({ onPrevious, onNext }: { onPrevious?: () => void; onNext?: () => void }) {
  const [pageSize, setPageSize] = useState(50);
  return (
    <Pagination
      pageSize={pageSize}
      pageSizes={[25, 50, 100]}
      onPageSizeChange={setPageSize}
      onPrevious={onPrevious}
      onNext={onNext}
    />
  );
}

// Spec 0001 UX-14: Previous, Next, and the page size, in three engines.
describe("Pagination", () => {
  it("0001 UX-14: Previous and Next call back, and a page with nowhere to go says so and stays focusable", async () => {
    const onNext = vi.fn<() => void>();
    await renderInRouter(<Paged onNext={onNext} />);

    await page.getByRole("button", { name: "Next" }).click();
    expect(onNext).toHaveBeenCalledOnce();

    const previous = page.getByRole("button", { name: "Previous" });
    await expect.element(previous).toHaveAttribute("aria-disabled", "true");
    await expect.element(previous).toHaveAccessibleDescription("No more pages.");
    await expectNoAxeViolations();
  });

  it("0001 UX-14: picks 25, 50, or 100, and shows the choice", async () => {
    await renderInRouter(<Paged />);

    await page.getByRole("button", { name: "50 per page" }).click();
    await expect.element(page.getByRole("menuitemradio", { name: "50" })).toBeChecked();
    await expectNoAxeViolations();
    await page.getByRole("menuitemradio", { name: "25" }).click();

    await expect.element(page.getByRole("button", { name: "25 per page" })).toBeVisible();
  });

  it("0001 UX-14: is a named landmark, so paging can be reached by name", async () => {
    await renderInRouter(<Paged />);

    await expect.element(page.getByRole("navigation", { name: "Pagination" })).toBeVisible();
  });
});
