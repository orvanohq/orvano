import { describe, expect, it, vi } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { focusElement } from "@/test/focus";
import { renderInRouter } from "@/test/render";
import { Banner, InlineError } from "./banner";
import { Button } from "./button";
import { Progress, Spinner } from "./progress";
import { EmptyState, ErrorState } from "./states";
import { Badge, StatusChip } from "./status-chip";

// Spec 0015 AC-12, AC-15: banners, inline errors, the empty and error states, progress, and status chips (UX-20,
// UX-22, UX-24, UX-29), with axe in both themes, in three engines.

async function expectNoAxeViolationsInBothThemes() {
  for (const theme of ["light", "dark"]) {
    document.documentElement.dataset.theme = theme;
    // oxlint-disable-next-line no-await-in-loop -- axe runs one theme at a time
    await expectNoAxeViolations();
  }
  delete document.documentElement.dataset.theme;
}

describe("Banner", () => {
  it("0015 AC-12: is a status, or an alert for danger, and passes axe in every tone", async () => {
    const { screen } = await renderInRouter(
      <div>
        <Banner tone="info" title="Maintenance tonight">
          Realtime may reconnect once.
        </Banner>
        <Banner tone="warning" action={<Button size="sm">Upgrade plan</Button>}>
          Storefront is at 90% of its storage.
        </Banner>
        <Banner tone="success">Back online.</Banner>
        <Banner tone="neutral">Read only mode.</Banner>
        <Banner tone="danger">You're offline.</Banner>
      </div>,
    );

    await expect
      .poll(() => screen.getByRole("alert").element().textContent)
      .toBe("You're offline.");
    expect(screen.getByRole("status").elements()).toHaveLength(4);
    await expectNoAxeViolationsInBothThemes();
  });
});

describe("InlineError", () => {
  it("0015 AC-12: is an alert with what failed, the request ID, and Retry", async () => {
    const onRetry = vi.fn<() => void>();
    const { screen } = await renderInRouter(
      <InlineError
        error={{ kind: "server", requestId: "req_42" }}
        title="Couldn't save the table"
        onRetry={onRetry}
      />,
    );

    const alert = screen.getByRole("alert");
    await expect.poll(() => alert.element().textContent).toContain("Couldn't save the table");
    expect(alert.element().textContent).toContain("Request ID: req_42");
    await screen.getByRole("button", { name: "Retry" }).click();
    expect(onRetry).toHaveBeenCalledOnce();
    await expectNoAxeViolationsInBothThemes();
  });

  it("0001 UX-22: names the role a permission denial needs, and when to retry a rate limit", async () => {
    const { screen } = await renderInRouter(
      <div>
        <InlineError error={{ kind: "forbidden", requiredRole: "Admin" }} />
        <InlineError error={{ kind: "rate_limited", retryAfter: 30 }} />
      </div>,
    );

    await expect.element(screen.getByText("You need the Admin role for this.")).toBeVisible();
    await expect.element(screen.getByText("Try again in 30 seconds.")).toBeVisible();
  });
});

describe("EmptyState and ErrorState", () => {
  it("0015 AC-15: an empty state has its title, sentence, and one action; the icon is hidden", async () => {
    const { screen } = await renderInRouter(
      <EmptyState
        kind="no-results"
        title="No results"
        description="No tables match these filters."
        action={<Button>Clear filters</Button>}
      />,
    );

    await expect.element(screen.getByRole("heading", { name: "No results" })).toBeVisible();
    await expect.element(screen.getByRole("button", { name: "Clear filters" })).toBeVisible();
    expect(document.querySelector("svg")?.getAttribute("aria-hidden")).toBe("true");
    await expectNoAxeViolationsInBothThemes();
  });

  it("0001 UX-22: a failed page says what failed, offers Retry, and shows the copyable request ID", async () => {
    const onRetry = vi.fn<() => void>();
    const { screen } = await renderInRouter(
      <ErrorState
        error={{ kind: "server", requestId: "req_42" }}
        title="Couldn't load tables"
        onRetry={onRetry}
      />,
    );

    await expect
      .element(screen.getByRole("heading", { name: "Couldn't load tables" }))
      .toBeVisible();
    await expect.element(screen.getByRole("textbox", { name: "Request ID" })).toHaveValue("req_42");
    await expect
      .element(screen.getByRole("button", { name: "Copy Request ID" }))
      .toBeInTheDocument();
    const retry = screen.getByRole("button", { name: "Retry" });
    await focusElement(retry);
    await userEvent.keyboard("{Enter}");
    expect(onRetry).toHaveBeenCalledOnce();
    await expectNoAxeViolationsInBothThemes();
  });

  it("0001 UX-22: not found links back to its list and has no Retry", async () => {
    const { screen } = await renderInRouter(
      <ErrorState
        error={{ kind: "not_found" }}
        onRetry={() => undefined}
        backLink={<Button>Back to tables</Button>}
      />,
    );

    await expect.element(screen.getByRole("heading", { name: "Not found" })).toBeVisible();
    await expect.element(screen.getByRole("button", { name: "Back to tables" })).toBeVisible();
    expect(screen.getByRole("button", { name: "Retry" }).elements()).toHaveLength(0);
  });

  it("0001 UX-22: a plain thrown error reads as a failure with no request ID", async () => {
    const { screen } = await renderInRouter(<ErrorState error={new Error("boom")} />);

    await expect
      .element(screen.getByRole("heading", { name: "Something went wrong" }))
      .toBeVisible();
    expect(screen.getByRole("textbox", { name: "Request ID" }).elements()).toHaveLength(0);
  });
});

describe("Progress, Spinner, StatusChip, and Badge", () => {
  it("0015 AC-15: progress exposes its value; a spinner and the chips have names", async () => {
    const { screen } = await renderInRouter(
      <div>
        <Progress label="Uploading backup.sql" value={42} />
        <Progress label="Restoring backup" />
        <Spinner aria-label="Loading tables" />
        {(["queued", "running", "ready", "failed", "cancelled"] as const).map((status) => (
          <StatusChip key={status} status={status} />
        ))}
        {(["neutral", "info", "success", "warning", "danger"] as const).map((tone) => (
          <Badge key={tone} tone={tone}>
            {tone}
          </Badge>
        ))}
      </div>,
    );

    const upload = screen.getByRole("progressbar", { name: "Uploading backup.sql" });
    await expect.element(upload).toHaveAttribute("aria-valuenow", "42");
    await expect.poll(() => upload.element().textContent).toContain("42%");
    await expect
      .element(screen.getByRole("progressbar", { name: "Restoring backup" }))
      .not.toHaveAttribute("aria-valuenow");
    await expect
      .element(screen.getByRole("progressbar", { name: "Loading tables" }))
      .toBeInTheDocument();
    for (const label of ["Queued", "Running", "Ready", "Failed", "Cancelled"]) {
      // oxlint-disable-next-line no-await-in-loop -- one chip at a time
      await expect.element(screen.getByText(label, { exact: true })).toBeVisible();
    }
    await expectNoAxeViolationsInBothThemes();
  });
});
