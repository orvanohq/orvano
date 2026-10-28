import { I18nProvider } from "@lingui/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { page, userEvent } from "vitest/browser";
import { render } from "vitest-browser-react";
import { resourceListLayout } from "@/shared/storage/items";
import { resetStorageForTests } from "@/shared/storage/stored";
import { activateLocale, i18n } from "@/shared/i18n";
import { focusElement } from "@/test/focus";
import { ResourceLayout } from "./resource-layout";
import { ShellContext } from "./shell-context";

// Spec 0015 SHL-13, spec 0001 UX-2, spec 0003 STO-2: one ResourceLayout element that stays mounted while its
// service or project changes (a layout route shared by services) reads and saves each one's own layout.

const userId = "u1";
const databases = { userId, projectId: "storefront", service: "databases" };
const storage = { userId, projectId: "storefront", service: "storage" };
const billingStorage = { userId, projectId: "billing", service: "storage" };

function Harness({ projectId, service }: { projectId: string; service: string }) {
  return (
    <I18nProvider i18n={i18n}>
      <div style={{ display: "flex", width: 1280 }}>
        <ShellContext
          value={{
            userId,
            projectId,
            isWide: true,
            listDrawerOpen: false,
            setListDrawerOpen: () => undefined,
          }}
        >
          <ResourceLayout service={service} label="Items" resizeLabel="Resize item list">
            <p>list</p>
          </ResourceLayout>
        </ShellContext>
        <main style={{ flex: 1 }} />
      </div>
    </I18nProvider>
  );
}

afterEach(() => {
  vi.restoreAllMocks();
  for (const ids of [databases, storage, billingStorage]) resourceListLayout.remove(ids);
  resetStorageForTests();
});

describe("ResourceLayout", () => {
  it("0015 AC-10: re-reads the saved width when the service, then the project, changes", async () => {
    await page.viewport(1280, 800);
    if (!i18n.locale) await activateLocale("en");
    resourceListLayout.write(databases, { collapsed: false, width: 344 });
    resourceListLayout.write(storage, { collapsed: false, width: 232 });
    resourceListLayout.write(billingStorage, { collapsed: false, width: 408 });
    const write = vi.spyOn(resourceListLayout, "write");

    const screen = await render(<Harness projectId="storefront" service="databases" />);
    const handle = screen.getByRole("separator", { name: "Resize item list" });
    await expect.element(handle).toHaveAttribute("aria-valuenow", "344");
    const separator = handle.element();
    await focusElement(handle);

    await screen.rerender(<Harness projectId="storefront" service="storage" />);
    await expect.element(handle).toHaveAttribute("aria-valuenow", "232");
    expect(handle.element()).toBe(separator); // the same element: no remount, so focus stays
    expect(document.activeElement).toBe(separator);

    await screen.rerender(<Harness projectId="billing" service="storage" />);
    await expect.element(handle).toHaveAttribute("aria-valuenow", "408");
    expect(document.activeElement).toBe(separator);
    expect(write).not.toHaveBeenCalled(); // switching only reads

    // A key change now saves under billing's key only.
    await userEvent.keyboard("{ArrowRight}");
    await expect.element(handle).toHaveAttribute("aria-valuenow", "424");
    expect(write).toHaveBeenCalledExactlyOnceWith(billingStorage, { collapsed: false, width: 424 });
    expect(resourceListLayout.read(databases).width).toBe(344);
    expect(resourceListLayout.read(storage).width).toBe(232);
  });

  it("0015 AC-10: a service with nothing saved starts at the default, not the previous layout", async () => {
    await page.viewport(1280, 800);
    if (!i18n.locale) await activateLocale("en");
    resourceListLayout.write(databases, { collapsed: true, width: 344 });
    const write = vi.spyOn(resourceListLayout, "write");

    const screen = await render(<Harness projectId="storefront" service="databases" />);
    await expect.element(screen.getByRole("button", { name: "Expand list" })).toBeVisible();

    await screen.rerender(<Harness projectId="storefront" service="storage" />);
    await expect
      .element(screen.getByRole("separator", { name: "Resize item list" }))
      .toHaveAttribute("aria-valuenow", "280");
    expect(write).not.toHaveBeenCalled();
  });
});
