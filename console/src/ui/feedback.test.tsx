import type { ReactNode } from "react";
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

describe("error wording by kind (UX-22)", () => {
  const cases: { name: string; error: unknown; title: string; body: string }[] = [
    {
      name: "a network failure asks you to check the connection",
      error: { kind: "network" },
      title: "Couldn't reach the server",
      body: "Check your connection and try again.",
    },
    {
      name: "a timeout reads like a network failure",
      error: { kind: "timeout" },
      title: "Couldn't reach the server",
      body: "Check your connection and try again.",
    },
    {
      name: "a permission denial with no role asks an owner",
      error: { kind: "forbidden" },
      title: "You don't have access",
      body: "Ask an owner of this project for access.",
    },
    {
      name: "a rate limit with no Retry-After asks you to wait",
      error: { kind: "rate_limited" },
      title: "Too many requests",
      body: "Wait a moment, then try again.",
    },
    {
      name: "a rate limit of one second is singular",
      error: { kind: "rate_limited", retryAfter: 1 },
      title: "Too many requests",
      body: "Try again in 1 second.",
    },
    {
      name: "a fractional Retry-After rounds up",
      error: { kind: "rate_limited", retryAfter: 2.2 },
      title: "Too many requests",
      body: "Try again in 3 seconds.",
    },
    {
      name: "an unknown kind with no request ID asks you to try again",
      error: { kind: "conflict" },
      title: "Something went wrong",
      body: "Try again in a moment.",
    },
    {
      name: "an empty request ID counts as none",
      error: { kind: "server", requestId: "" },
      title: "Something went wrong",
      body: "Try again in a moment.",
    },
    {
      name: "a thrown string reads as a plain failure",
      error: "boom",
      title: "Something went wrong",
      body: "Try again in a moment.",
    },
    {
      name: "a thrown null reads as a plain failure",
      error: null,
      title: "Something went wrong",
      body: "Try again in a moment.",
    },
    {
      name: "a non numeric Retry-After is ignored",
      error: { kind: "rate_limited", retryAfter: "30" },
      title: "Too many requests",
      body: "Wait a moment, then try again.",
    },
  ];

  for (const { name, error, title, body } of cases) {
    it(`0001 UX-22: ${name}`, async () => {
      const { screen } = await renderInRouter(<InlineError error={error} />);

      const alert = screen.getByRole("alert");
      await expect.poll(() => alert.element().textContent).toContain(title);
      expect(alert.element().textContent).toContain(body);
      expect(alert.element().textContent).not.toContain("Request ID");
    });
  }

  it("0001 UX-22: a network failure keeps the caller's title", async () => {
    const { screen } = await renderInRouter(
      <InlineError error={{ kind: "network" }} title="Couldn't load tables" />,
    );

    await expect.element(screen.getByText("Couldn't load tables")).toBeVisible();
  });

  it("0015 AC-12: children replace the wording, and no Retry shows without onRetry", async () => {
    const { screen } = await renderInRouter(
      <InlineError error={{ kind: "server" }}>The column name is taken.</InlineError>,
    );

    await expect.element(screen.getByText("The column name is taken.")).toBeVisible();
    expect(screen.getByRole("button").elements()).toHaveLength(0);
  });
});

describe("states at their edges", () => {
  it("0001 UX-22: forbidden names the role, shows no Retry, and no back link", async () => {
    const { screen } = await renderInRouter(
      <ErrorState
        error={{ kind: "forbidden", requiredRole: "Owner" }}
        onRetry={() => undefined}
        backLink={<Button>Back to tables</Button>}
      />,
    );

    await expect
      .element(screen.getByRole("heading", { name: "You don't have access" }))
      .toBeVisible();
    await expect.element(screen.getByText("You need the Owner role for this.")).toBeVisible();
    expect(screen.getByRole("button").elements()).toHaveLength(0);
  });

  it("0001 UX-22: not found without a back link shows only its words", async () => {
    const { screen } = await renderInRouter(<ErrorState error={{ kind: "not_found" }} />);

    await expect.element(screen.getByRole("heading", { name: "Not found" })).toBeVisible();
    expect(screen.getByRole("button").elements()).toHaveLength(0);
  });

  it("0001 UX-20: a first use state shows the quick start below its action", async () => {
    const { screen } = await renderInRouter(
      <EmptyState
        kind="first-use"
        title="Create your first table"
        description="Tables hold your rows."
        action={<Button>Create table</Button>}
      >
        <p>npm install @orvano/sdk</p>
      </EmptyState>,
    );

    const action = screen.getByRole("button", { name: "Create table" }).element();
    const quickStart = screen.getByText("npm install @orvano/sdk").element();
    await expect.element(quickStart).toBeVisible();
    expect(
      action.compareDocumentPosition(quickStart) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy();
  });

  it("0001 UX-20: a not allowed state with no action renders no button", async () => {
    const { screen } = await renderInRouter(
      <EmptyState kind="not-allowed" title="No access" description="Ask an owner." />,
    );

    await expect.element(screen.getByRole("heading", { name: "No access" })).toBeVisible();
    expect(screen.getByRole("button").elements()).toHaveLength(0);
    await expectNoAxeViolationsInBothThemes();
  });

  it("0015 AC-15: progress shows 0% and 100% at its ends", async () => {
    const { screen } = await renderInRouter(
      <div>
        <Progress label="Starting" value={0} />
        <Progress label="Done" value={100} />
      </div>,
    );

    const start = screen.getByRole("progressbar", { name: "Starting" });
    const done = screen.getByRole("progressbar", { name: "Done" });
    await expect.element(start).toHaveAttribute("aria-valuenow", "0");
    await expect.poll(() => start.element().textContent).toContain("0%");
    await expect.element(done).toHaveAttribute("aria-valuenow", "100");
    await expect.poll(() => done.element().textContent).toContain("100%");
  });

  // WCAG 1.4.10 and spec 0015 AC-8 (reflow at 320 px): a resource name can be one long word, here a 63 character
  // Postgres identifier, the longest a table name can be.
  // Their text blocks set `overflow-wrap: anywhere`, which also lowers a centered flex item's minimum width.
  const long = "customer_order_line_items_archive_2024_q4_eu_west_replica_backu";
  const narrow = async (element: ReactNode) => {
    const { screen } = await renderInRouter(<div style={{ width: 320 }}>{element}</div>);
    await expect.element(screen.getByText(long).first()).toBeInTheDocument();
    const box = document.querySelector<HTMLElement>("[role=status], [role=alert], section");
    if (!box) throw new Error("nothing rendered");
    return { scrollWidth: box.scrollWidth, clientWidth: box.clientWidth };
  };

  it("0015 AC-8: an inline error wraps a long request ID and title inside 320 px", async () => {
    const size = await narrow(
      <InlineError error={{ kind: "server", requestId: long }} title={long} />,
    );
    expect(size.scrollWidth).toBeLessThanOrEqual(size.clientWidth);
  });

  it("0015 AC-8: an empty state wraps a long title inside 320 px", async () => {
    const size = await narrow(<EmptyState kind="no-results" title={long} description={long} />);
    expect(size.scrollWidth).toBeLessThanOrEqual(size.clientWidth);
  });

  it("0015 AC-8: an error state wraps a long title inside 320 px", async () => {
    const size = await narrow(<ErrorState error={new Error("boom")} title={long} />);
    expect(size.scrollWidth).toBeLessThanOrEqual(size.clientWidth);
  });

  it("0015 AC-8: a banner wraps a long unbroken word inside 320 px", async () => {
    const size = await narrow(
      <Banner tone="warning" title={long}>
        {long}
      </Banner>,
    );
    expect(size.scrollWidth).toBeLessThanOrEqual(size.clientWidth);
  });
});
