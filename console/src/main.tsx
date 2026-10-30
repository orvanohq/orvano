import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { I18nProvider } from "@lingui/react";
import { QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "@tanstack/react-router";
import { consoleConfigQuery } from "@/features/console-config/api/queries";
import { createQueryClient } from "@/shared/api/query-client";
import { activateLocale, i18n, resolveLocale, sourceLocale } from "@/shared/i18n";
import { reportConsoleError } from "@/shared/report";
// Spec 0003 RTE-10 step 1, SEC-2: Zod never compiles code at runtime, so the CSP needs no unsafe-eval. `@/shared/zod`
// sets that; calling it here too keeps it in place for any schema built later.
import { z } from "@/shared/zod";
import { startThemeSync } from "@/shared/stores/theme";
import { BootErrorPage } from "@/ui/error-page";
import { LocaleProvider } from "@/ui/locale-provider";
import { createAppRouter } from "./router";
import "@/ui/theme.css"; // spec 0015 STY-1: the one stylesheet

z.config({ jitless: true });

const rootElement = document.getElementById("root");
if (!rootElement) throw new Error("index.html has no #root element");

const root = createRoot(rootElement, {
  onCaughtError: reportConsoleError, // spec 0003 WEB-10, FLT-4
  onUncaughtError: reportConsoleError,
});

startThemeSync(); // spec 0015 THM-4: keeps <html data-theme> current after theme-boot.js

// Spec 0003 RTE-10: boot before the first render. The runtime config loads beside the locale into the query cache
// (spec 0016 FRS-2); feature 13 adds the session request. If booting fails, the plain boot error page renders instead
// of the router (FLT-2, spec 0015 AC-21).
try {
  const queryClient = createQueryClient();
  await Promise.all([
    activateLocale(resolveLocale(navigator.languages)),
    queryClient.query(consoleConfigQuery()),
  ]);
  const router = createAppRouter({ queryClient });

  root.render(
    <StrictMode>
      <I18nProvider i18n={i18n}>
        <QueryClientProvider client={queryClient}>
          <RouterProvider router={router} />
        </QueryClientProvider>
      </I18nProvider>
    </StrictMode>,
  );
} catch (error) {
  // The en catalog is bundled (I18N-3), so the error page always has its strings. The router's root route gives React
  // Aria the locale (I18N-6); without the router, this page does.
  await activateLocale(sourceLocale);
  root.render(
    <StrictMode>
      <I18nProvider i18n={i18n}>
        <LocaleProvider locale={sourceLocale}>
          <BootErrorPage error={error} />
        </LocaleProvider>
      </I18nProvider>
    </StrictMode>,
  );
}
