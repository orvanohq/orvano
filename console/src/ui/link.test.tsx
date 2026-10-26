import { describe, expect, it, vi } from "vitest";
import { userEvent } from "vitest/browser";
import { expectNoAxeViolations } from "@/test/axe";
import { renderInRouter } from "@/test/render";
import { Link } from "./link";

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
