import { useLingui } from "@lingui/react/macro";
import { BrandLink } from "./brand-mark";
import { PrimarySidebar } from "./primary-sidebar";
import { SkipLink } from "./skip-link";
import type { AppShellProps, Crumb, ShellModel } from "./types";

/**
 * Spec 0015 SHL-3: the frame every signed in screen sits in. Landmarks in order: the skip link, the `header` top bar
 * with the "Breadcrumb" `nav`, the scope's primary `nav`, an optional resource list, and one `main`. This is the
 * milestone 1 frame: the switchers, top bar controls, collapse, and narrow drawers arrive with milestone 2.
 */
export function AppShell({ model, crumbs, resourceList, children }: AppShellProps) {
  const { t } = useLingui();
  return (
    <div className="grid h-full grid-rows-[auto_minmax(0,1fr)] bg-bg">
      <SkipLink />
      <header className="flex h-topbar items-center gap-4 border-b bg-bg-subtle px-4">
        <BrandLink />
        <nav aria-label={t`Breadcrumb`} className="min-w-0 flex-1">
          <CrumbList model={model} crumbs={crumbs} />
        </nav>
      </header>
      <div className="flex min-h-0">
        <PrimarySidebar scope={model.scope} nav={model.nav} />
        {resourceList}
        <main
          id="main"
          tabIndex={-1}
          className="min-w-0 flex-1 overflow-auto px-4 py-6 outline-none lg:px-6"
        >
          {children}
        </main>
      </div>
    </div>
  );
}

/** A plain list of the crumbs until `Breadcrumbs` and the switchers land (milestone 2). */
function CrumbList({ model, crumbs }: { model: ShellModel; crumbs: readonly Crumb[] }) {
  const label = (crumb: Crumb): string =>
    crumb.kind === "page"
      ? crumb.label
      : ((crumb.kind === "org" ? model.org?.name : model.project?.name) ?? "");
  return (
    <ol className="flex items-center gap-2 truncate text-base">
      {crumbs.map((crumb, index) => {
        const last = index === crumbs.length - 1;
        return (
          <li
            key={`${crumb.kind}-${String(index)}`}
            aria-current={last ? "page" : undefined}
            className={last ? "font-medium text-fg" : "text-fg-muted"}
          >
            {label(crumb)}
          </li>
        );
      })}
    </ol>
  );
}
