import { I18nProvider } from "@lingui/react";
import { createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { page } from "vitest/browser";
import { render } from "vitest-browser-react";
import { layout, resourceListLayout, themePreference } from "@/shared/storage/items";
import { resetStorageForTests } from "@/shared/storage/stored";
import { resetLayoutStoresForTests } from "@/shared/stores/layout";
import { activateLocale, i18n } from "@/shared/i18n";
import { LocaleProvider } from "@/ui";
import { project, user } from "./fixtures";
import { createGalleryRouter } from "./router";

/** Test only: renders a gallery scene as the gallery does (without its toolbar), optionally right to left (RTL-1). */
export async function renderScene(path: string, options: { direction?: "ltr" | "rtl" } = {}) {
  if (!i18n.locale) await activateLocale("en");
  const rtl = options.direction === "rtl";
  document.documentElement.dir = rtl ? "rtl" : "ltr";
  const router = createGalleryRouter({
    history: createMemoryHistory({ initialEntries: [path] }),
    toolbar: false,
  });
  await router.load();
  const screen = await render(
    <I18nProvider i18n={i18n}>
      <LocaleProvider locale={rtl ? "ar-XB" : "en-US"}>
        <RouterProvider router={router} />
      </LocaleProvider>
    </I18nProvider>,
  );
  return { router, screen };
}

/** The shell's layouts: wide (from 64rem) and narrow. */
export const WIDE = { width: 1280, height: 800 };
export const NARROW = { width: 800, height: 800 };

export async function setViewport(size: { width: number; height: number }) {
  await page.viewport(size.width, size.height);
}

/** Forgets stored layouts and the theme and density on <html>, between tests. */
export function resetShellState() {
  layout.remove({ userId: user.id });
  resourceListLayout.remove({ userId: user.id, projectId: project.id, service: "databases" });
  themePreference.remove(undefined);
  resetStorageForTests();
  resetLayoutStoresForTests();
  const html = document.documentElement;
  delete html.dataset.theme;
  delete html.dataset.density;
  html.dir = "ltr";
  html.style.colorScheme = "";
}

/** Presses Tab until `element` has focus, at most `limit` times (a fresh Firefox frame can take one Tab first). */
export async function tabTo(element: Element, limit = 30) {
  const { userEvent } = await import("vitest/browser");
  for (let index = 0; index < limit && document.activeElement !== element; index++) {
    await userEvent.tab();
  }
}
