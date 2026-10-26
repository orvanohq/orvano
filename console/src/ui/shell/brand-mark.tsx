import { useLingui } from "@lingui/react/macro";
import { Link } from "../link";

/**
 * Spec 0015 SHL-7: the geometric mark in `accent`, plus the word "Orvano" (a brand name, never translated). The
 * mark is decorative; `markOnly` is for the icon rail. Replacing it with a real logo is a change to this file.
 */
export function BrandMark({ markOnly = false }: { markOnly?: boolean }) {
  return (
    <span aria-hidden="true" className="inline-flex items-center gap-2">
      <svg viewBox="0 0 24 24" className="size-5 shrink-0 fill-accent" focusable="false">
        <path fillRule="evenodd" d="M12 2 22 12 12 22 2 12Z M12 8 16 12 12 16 8 12Z" />
      </svg>
      {!markOnly && <span className="text-md font-semibold text-fg">Orvano</span>}
    </span>
  );
}

/** The brand mark as a link to the console home, named "Orvano home". */
export function BrandLink({ markOnly = false }: { markOnly?: boolean }) {
  const { t } = useLingui();
  return (
    <Link to="/" variant="nav" aria-label={t`Orvano home`}>
      <BrandMark markOnly={markOnly} />
    </Link>
  );
}
