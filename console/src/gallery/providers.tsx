import { useEffect, type ReactNode } from "react";
import { I18nProvider } from "@lingui/react";
import { activateLocale, i18n } from "@/shared/i18n";
import { LocaleProvider } from "@/ui";
import { useSettings } from "./settings";

/**
 * Applies the toolbar's settings: the Lingui catalog, and the direction. Right to left gives React Aria the real `ar`
 * locale while Lingui keeps its catalog (RTL-1); never the pseudo `ar-XB`, which WebKit resolves as left to right. The
 * pseudo locale's `lang` is `en-XA`, a valid pseudo tag, rather than the catalog's name.
 */
export function GalleryProviders({ children }: { children: ReactNode }) {
  const { locale, direction } = useSettings();

  useEffect(() => {
    const apply = async () => {
      await activateLocale(locale);
      if (locale === "pseudo-LOCALE") document.documentElement.lang = "en-XA";
      document.documentElement.dir = direction;
    };
    void apply();
  }, [locale, direction]);

  return (
    <I18nProvider i18n={i18n}>
      <LocaleProvider locale={direction === "rtl" ? "ar" : "en-US"}>{children}</LocaleProvider>
    </I18nProvider>
  );
}
