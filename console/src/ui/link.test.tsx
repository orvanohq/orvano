import { describe, expect, it, vi } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { renderInRouter } from "@/test/render";
import { ExternalLink, Link } from "./link";

// Spec 0003 RTE-6's "verify with feature 11": the createLink form keeps the router's preload and navigation.

const target = "/tables" as "/";

describe("Link", () => {
  it("0015 AC-15: preloads the target route on hover", async () => {
    const loader = vi.fn<() => void>();
    const { screen } = await renderInRouter(<Link to={target}>Tables</Link>, [
      { path: "/tables", loader },
    ]);

    await userEvent.hover(screen.getByRole("link", { name: "Tables" }));

    await vi.waitFor(() => {
      expect(loader).toHaveBeenCalledWith(expect.objectContaining({ preload: true }));
    });
  });

  it("0015 AC-15: preloads the target route on keyboard focus", async () => {
    const loader = vi.fn<() => void>();
    await renderInRouter(<Link to={target}>Tables</Link>, [{ path: "/tables", loader }]);

    await userEvent.tab();

    await vi.waitFor(() => {
      expect(loader).toHaveBeenCalledWith(expect.objectContaining({ preload: true }));
    });
  });

  it("0015 AC-15: navigates with Enter and marks the current page", async () => {
    const { router, screen } = await renderInRouter(
      <>
        <Link to="/">Home</Link>
        <Link to={target}>Tables</Link>
      </>,
      [{ path: "/tables" }],
    );

    await expect
      .element(screen.getByRole("link", { name: "Home" }))
      .toHaveAttribute("aria-current", "page");
    await userEvent.tab();
    await userEvent.tab();
    await userEvent.keyboard("{Enter}");

    await vi.waitFor(() => {
      expect(router.state.location.pathname).toBe("/tables");
    });
  });

  it("0015 AC-15: a keyboard press keeps the link's history state (RTE-7's keepFocus)", async () => {
    const { router } = await renderInRouter(
      <Link to={target} state={{ keepFocus: true } as never}>
        Tables
      </Link>,
      [{ path: "/tables" }],
    );

    await userEvent.tab();
    await userEvent.keyboard("{Enter}");

    await vi.waitFor(() => {
      expect(router.state.location.pathname).toBe("/tables");
    });
    expect(router.state.location.state).toMatchObject({ keepFocus: true });
  });

  it("0015 AC-15: inline and nav links pass axe in both themes", async () => {
    await renderInRouter(
      <p>
        Read{" "}
        <Link to={target} variant="inline">
          the docs
        </Link>{" "}
        or go{" "}
        <Link to="/" variant="nav">
          home
        </Link>
        .
      </p>,
      [{ path: "/tables" }],
    );
    for (const theme of ["light", "dark"]) {
      document.documentElement.dataset.theme = theme;
      await expectNoAxeViolations();
    }
    delete document.documentElement.dataset.theme;
  });
});

describe("ExternalLink", () => {
  it("0015 SHL-9: opens a web address in a new tab, at least 24 px high (UX-32)", async () => {
    const { screen } = await renderInRouter(
      <ExternalLink href="https://docs.example.com/">Open the docs</ExternalLink>,
    );
    const link = screen.getByRole("link", { name: /Open the docs/ });
    await expect.element(link).toHaveAttribute("href", "https://docs.example.com/");
    await expect.element(link).toHaveAttribute("target", "_blank");
    await expect.element(link).toHaveAttribute("rel", "noopener noreferrer");
    expect(link.element().getBoundingClientRect().height).toBeGreaterThanOrEqual(24);
    await expectNoAxeViolations();
  });

  it("0003 SEC-2: renders any other address as plain text, never a link", async () => {
    for (const href of [
      // oxlint-disable-next-line no-script-url -- the value under test
      "javascript:alert(1)",
      "data:text/html,<p>hi</p>",
      "/relative/path",
      "not a url",
    ]) {
      // oxlint-disable-next-line no-await-in-loop -- one render per address
      const { screen } = await renderInRouter(
        <ExternalLink href={href}>Open the docs</ExternalLink>,
      );
      // oxlint-disable-next-line no-await-in-loop -- waits for this render before checking it
      await expect.element(screen.getByText("Open the docs")).toBeVisible();
      expect(document.querySelector("a")).toBeNull();
      // oxlint-disable-next-line no-await-in-loop -- unmounts before the next address renders
      await screen.unmount();
    }
  });
});
