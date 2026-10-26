import type { ReactNode } from "react";
import { I18nProvider } from "react-aria-components";

/**
 * Gives React Aria its locale, which sets its own strings, number and date formats, and direction (spec 0003
 * I18N-6). The gallery and the right to left test pass `ar-XB` to prove right to left without a catalog (RTL-1).
 */
export function LocaleProvider({ locale, children }: { locale: string; children: ReactNode }) {
  return <I18nProvider locale={locale}>{children}</I18nProvider>;
}
