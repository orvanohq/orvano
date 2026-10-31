import { I18nProvider } from "@lingui/react";
import { QueryClient } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router";
import { afterEach, describe, expect, it } from "vitest";
import { userEvent } from "vitest/browser";
import { render } from "vitest-browser-react";
import { consoleConfigQuery } from "@/features/console-config/api/queries";
import { messages as sourceMessages } from "@/locales/en/messages.po";
import { routeTree } from "@/routeTree.gen";
import { activateLocale, getDirection, i18n } from "@/shared/i18n";
import { focusElement } from "@/test/focus";
import { renderWithProviders } from "@/test/render";
import { Progress, Tab, TabList, TabPanel, Tabs } from "@/ui";

// Spec 0003 RTE-3: the router runs under the /console base path and handles unknown paths itself.
describe("createAppRouter", () => {
  it("shows a not found view when the path matches no route", async () => {
    const screen = await renderWithProviders("/no/such/page");

    await expect.element(screen.getByText(/not found/i)).toBeVisible();
  });
});

// Spec 0016 FRS-3, AC-5: while setup is open every path lands on the setup page; once it is done, /setup goes home.
describe("the setup redirect", () => {
  it.each(["/", "/no/such/page", "/setupx", "/setup/extra", "/gallery"])(
    "sends %s to /setup while setup is required",
    async (url) => {
      const screen = await renderWithProviders(url, { config: { setupRequired: true } });

      await expect
        .element(screen.getByRole("heading", { level: 1, name: "Set up Orvano" }))
        .toBeVisible();
      expect(screen.router.state.location.pathname).toBe("/setup");
    },
  );

  it.each(["/setup", "/setup/", "/setup?from=mail"])(
    "keeps %s while setup is required",
    async (url) => {
      const screen = await renderWithProviders(url, { config: { setupRequired: true } });

      await expect
        .element(screen.getByRole("heading", { level: 1, name: "Set up Orvano" }))
        .toBeVisible();
      expect(screen.router.state.location.pathname.replace(/\/$/, "")).toBe("/setup");
    },
  );

  it.each(["/setup", "/setup/"])(
    "sends %s to the console root once setup is complete",
    async (url) => {
      const screen = await renderWithProviders(url, { config: { setupRequired: false } });

      await expect
        .element(screen.getByRole("heading", { level: 1, name: "Orvano console" }))
        .toBeVisible();
      expect(screen.router.state.location.pathname).toBe("/");
    },
  );
});

// Spec 0003 I18N-6, spec 0015 RTL-1: the root route gives React Aria the console's locale, and follows it when it
// changes. The probe page joins the real route tree, so it renders inside the real root route.
function LocaleProbe() {
  return (
    <>
      <Progress label="Upload" value={50} />
      <Tabs>
        <TabList aria-label="SDK">
          <Tab id="js">JavaScript</Tab>
          <Tab id="dart">Dart</Tab>
        </TabList>
        <TabPanel id="js">JavaScript panel</TabPanel>
        <TabPanel id="dart">Dart panel</TabPanel>
      </Tabs>
    </>
  );
}

const probe = createRoute({
  getParentRoute: () => routeTree,
  path: "/locale-probe",
  component: LocaleProbe,
});
const probeTree = routeTree.addChildren({ ...routeTree.children, probe });

async function renderProbe() {
  await activateLocale("en");
  const queryClient = new QueryClient();
  queryClient.setQueryData(consoleConfigQuery().queryKey, { setupRequired: false });
  const router = createRouter({
    routeTree: probeTree,
    context: { queryClient },
    history: createMemoryHistory({ initialEntries: ["/locale-probe"] }),
  });
  await router.load();
  return render(
    <I18nProvider i18n={i18n}>
      <RouterProvider router={router} />
    </I18nProvider>,
  );
}

/** A right to left locale with no catalog of its own: Lingui keeps the English messages under the `ar` tag. */
function activateArabic() {
  // The real `ar`, not the pseudo `ar-XB`: CI's WebKit resolves ar-XB as left to right.
  i18n.loadAndActivate({ locale: "ar", messages: sourceMessages });
  document.documentElement.lang = "ar";
  document.documentElement.dir = getDirection("ar");
}

describe("the console locale in React Aria", () => {
  afterEach(async () => {
    await activateLocale("en");
  });

  it("0003 I18N-6: React Aria's value text follows the console locale, not the browser's", async () => {
    // The value text is formatted for React Aria's locale. Its word strings for `ar` are pruned with every locale that
    // doesn't ship (vite.config.ts) and fall back to en-US, so they can't show it.
    const screen = await renderProbe();
    const progress = screen.getByRole("progressbar", { name: "Upload" });
    const english = new Intl.NumberFormat("en-US", { style: "percent" }).format(0.5);
    const arabic = new Intl.NumberFormat("ar", { style: "percent" }).format(0.5);
    expect(arabic).not.toBe(english);

    await expect.element(progress).toHaveAttribute("aria-valuetext", english);
    activateArabic();
    await expect.element(progress).toHaveAttribute("aria-valuetext", arabic);
  });

  it("0015 RTL-1: in a right to left console locale the arrow keys are mirrored", async () => {
    const screen = await renderProbe();
    const js = screen.getByRole("tab", { name: "JavaScript" });
    const dart = screen.getByRole("tab", { name: "Dart" });

    activateArabic();
    // The progress bar's text changes once React Aria has the new locale, so the keys below read it too.
    await expect
      .element(screen.getByRole("progressbar", { name: "Upload" }))
      .toHaveAttribute(
        "aria-valuetext",
        new Intl.NumberFormat("ar", { style: "percent" }).format(0.5),
      );
    await focusElement(js);
    await userEvent.keyboard("{ArrowLeft}");
    await expect.element(dart).toHaveFocus();
    await userEvent.keyboard("{ArrowRight}");
    await expect.element(js).toHaveFocus();
  });
});
