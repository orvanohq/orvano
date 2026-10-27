import { describe, expect, it } from "vitest";
import { expectNoAxeViolations } from "@/test/axe";
import { focusElement } from "@/test/focus";
import { renderInRouter } from "@/test/render";
import { formatRelativeTime } from "@/shared/format";
import { RelativeTime } from "./relative-time";

// Spec 0015 AC-15, PRM-11, spec 0001 UX-34: relative up to 7 days, then a date, in a `<time datetime>`; the exact
// local time and UTC on focus. In three engines.

const minute = 60_000;
const day = 24 * 60 * minute;

describe("RelativeTime", () => {
  it("0015 PRM-11: is a focusable time element whose tooltip shows the local time and UTC", async () => {
    const moment = new Date(Date.now() - 3 * minute);
    const { screen } = await renderInRouter(<RelativeTime datetime={moment.toISOString()} />);

    const time = screen.getByText("3 minutes ago");
    await expect.element(time).toHaveAttribute("datetime", moment.toISOString());
    expect(time.element().tagName).toBe("TIME");

    await focusElement(time);
    const tooltip = screen.getByRole("tooltip");
    await expect.element(tooltip).toBeVisible();
    await expect.poll(() => tooltip.element().textContent).toMatch(/UTC$/);
    const describedBy = time.element().getAttribute("aria-describedby") ?? "";
    expect(document.getElementById(describedBy)?.textContent).toMatch(/UTC$/);
    await expectNoAxeViolations();
  });

  it("0001 UX-34: past 7 days shows a medium date", async () => {
    const moment = new Date(Date.now() - 40 * day);
    const { screen } = await renderInRouter(<RelativeTime datetime={moment} />);
    const expected = new Intl.DateTimeFormat("en", { dateStyle: "medium" }).format(moment);

    await expect.element(screen.getByText(expected)).toBeVisible();
  });
});

describe("formatRelativeTime", () => {
  const now = new Date("2026-10-27T12:00:00Z");
  const at = (offset: number) => formatRelativeTime(new Date(now.getTime() + offset), now);

  it("0015 PRM-11: words the past and the future with numeric auto", () => {
    expect(at(-10_000)).toBe("now");
    expect(at(-3 * minute)).toBe("3 minutes ago");
    expect(at(5 * minute)).toBe("in 5 minutes");
    expect(at(-2 * 60 * minute)).toBe("2 hours ago");
    expect(at(-day)).toBe("yesterday");
    expect(at(-6 * day)).toBe("6 days ago");
  });

  it("0001 UX-34: switches to a date at 7 days", () => {
    expect(at(-7 * day)).toBe("Oct 20, 2026");
  });
});
