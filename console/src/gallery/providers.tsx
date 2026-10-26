import { useEffect, type ReactNode } from "react";
import { I18nProvider } from "@lingui/react";
import { activateLocale, i18n } from "@/shared/i18n";
import { LocaleProvider } from "@/ui";
import { useSettings } from "./settings";

/**
 * Applies the toolbar's settings: the Lingui catalog, and the direction. Right to left gives React Aria the `ar-XB` locale while Lingui keeps its
 * catalog (RTL-1).
 */
export function GalleryProviders({ children }: { children: ReactNode }) {
  const { locale, direction } = useSettings();

  useEffect(() => {
    const apply = async () => {
      await activateLocale(locale);
      document.documentElement.dir = direction;
    };
    void apply();
  }, [locale, direction]);

  return (
    <I18nProvider i18n={i18n}>
      <LocaleProvider locale={direction === "rtl" ? "ar-XB" : "en-US"}>{children}</LocaleProvider>
    </I18nProvider>
  );
}
