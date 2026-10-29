import { useRef, useState, useSyncExternalStore } from "react";
import { useLingui } from "@lingui/react/macro";
import { useRouter } from "@tanstack/react-router";
import {
  Breadcrumb,
  Breadcrumbs as AriaBreadcrumbs,
  Header,
  MenuSection as AriaMenuSection,
} from "react-aria-components";
import { IconButton } from "../button";
import { Icon } from "../icon";
import { ChevronEndIcon, MoreIcon } from "../icons";
import { Link } from "../link";
import { Menu, MenuItem, MenuTrigger } from "../menu";
import { linkOptions } from "./link-options";
import { OrgSwitcher, ProjectSwitcher, SwitcherPopover } from "./switcher";
import type { Crumb, ShellModel } from "./types";

export interface BreadcrumbsProps {
  model: ShellModel;
  crumbs: readonly Crumb[];
  /** Below 64rem every crumb except the first and last collapses into a menu (UX-4). */
  collapsed?: boolean;
}

// SHL-4: below 25rem (400 px) only the collapsed menu and the current page show. Same unit as Tailwind's breakpoints,
// so CSS and JavaScript agree at any default font size.
function narrowMedia(): MediaQueryList {
  return window.matchMedia("(width < 25rem)");
}

function subscribeNarrow(onChange: () => void): () => void {
  const list = narrowMedia();
  list.addEventListener("change", onChange);
  return () => {
    list.removeEventListener("change", onChange);
  };
}

function useIsNarrow(): boolean {
  return useSyncExternalStore(subscribeNarrow, () => narrowMedia().matches);
}

/**
 * Spec 0015 AC-9: the `nav` labeled "Breadcrumb" around React Aria's `Breadcrumbs` list. Org and project crumbs
 * show their names from the model and open the switchers; page crumbs are links, except the last, which is
 * `aria-current="page"`. Below 64rem the middle crumbs collapse into a menu, and below 25rem every crumb but the
 * current one does (SHL-4).
 */
export function Breadcrumbs({ model, crumbs, collapsed = false }: BreadcrumbsProps) {
  const { t } = useLingui();
  const narrow = useIsNarrow();
  const last = { crumb: crumbs.at(-1), index: crumbs.length - 1 };
  let hidden: readonly Crumb[] = [];
  let shown: ({ crumb: Crumb | undefined; index: number } | null)[];
  if (narrow && crumbs.length > 1) {
    hidden = crumbs.slice(0, -1);
    shown = [null, last];
  } else if (collapsed && crumbs.length > 2) {
    hidden = crumbs.slice(1, -1);
    shown = [{ crumb: crumbs[0], index: 0 }, null, last];
  } else {
    shown = crumbs.map((crumb, index) => ({ crumb, index }));
  }

  return (
    <nav aria-label={t`Breadcrumb`} className="min-w-0 flex-1">
      <AriaBreadcrumbs className="flex min-w-0 items-center gap-1 text-base">
        {shown.map((entry) => {
          if (!entry) {
            return (
              <Breadcrumb key="more" className="flex shrink-0 items-center gap-1">
                <CollapsedCrumbs model={model} crumbs={hidden} />
                <Separator />
              </Breadcrumb>
            );
          }
          const { crumb, index } = entry;
          if (!crumb) return null;
          return (
            <Breadcrumb
              key={`${crumb.kind}-${String(index)}`}
              className="flex min-w-0 items-center gap-1"
            >
              {({ isCurrent }) => (
                <>
                  <CrumbContent model={model} crumb={crumb} isCurrent={isCurrent} />
                  {!isCurrent && <Separator />}
                </>
              )}
            </Breadcrumb>
          );
        })}
      </AriaBreadcrumbs>
    </nav>
  );
}

function Separator() {
  return (
    <span className="text-fg-muted">
      <Icon icon={ChevronEndIcon} flipInRtl />
    </span>
  );
}

function CrumbContent({
  model,
  crumb,
  isCurrent,
}: {
  model: ShellModel;
  crumb: Crumb;
  isCurrent: boolean;
}) {
  if (crumb.kind === "org" && model.org) {
    return (
      <OrgSwitcher
        name={model.org.name}
        currentId={model.org.id}
        search={model.searchOrgs}
        links={model.orgLinks}
        isCurrent={isCurrent}
      />
    );
  }
  if (crumb.kind === "project" && model.project) {
    return (
      <ProjectSwitcher
        name={model.project.name}
        currentId={model.project.id}
        search={model.searchProjects}
        links={model.projectLinks}
        isCurrent={isCurrent}
      />
    );
  }
  if (crumb.kind !== "page") return null;
  if (isCurrent || !crumb.link) {
    return (
      <span
        aria-current={isCurrent ? "page" : undefined}
        // SHL-4: the text is the full name, so the accessible name is too; the tooltip shows it when it truncates.
        title={crumb.label}
        className={isCurrent ? "truncate font-medium text-fg" : "truncate text-fg-muted"}
      >
        {crumb.label}
      </span>
    );
  }
  return (
    // SHL-4: the link keeps at least a 24 px target however far the row shrinks.
    <span className="min-w-6 truncate text-fg-muted">
      {/* Exact, so a crumb to an ancestor route isn't marked current too: only the last crumb is (AC-9). */}
      <Link variant="nav" {...linkOptions(crumb.link)} activeOptions={{ exact: true }}>
        {crumb.label}
      </Link>
    </span>
  );
}

/**
 * The collapsed crumbs in a menu. Org and project items open their switcher, anchored to the menu's button; a page
 * crumb with no link is plain text, not an item that does nothing.
 */
function CollapsedCrumbs({ model, crumbs }: { model: ShellModel; crumbs: readonly Crumb[] }) {
  const { t } = useLingui();
  const router = useRouter();
  const buttonRef = useRef<HTMLSpanElement>(null);
  const [switcher, setSwitcher] = useState<"org" | "project" | null>(null);

  return (
    <span ref={buttonRef} className="inline-flex">
      <MenuTrigger
        trigger={<IconButton icon={MoreIcon} aria-label={t`Show the full path`} size="sm" />}
      >
        <Menu aria-label={t`Path`}>
          {crumbs.map((crumb, index) => {
            const id = `${crumb.kind}-${String(index)}`;
            if (crumb.kind === "page") {
              const link = crumb.link;
              if (!link) {
                // A group holding only its label: read and shown as text, and the arrow keys pass it by.
                return (
                  <AriaMenuSection key={id} id={id}>
                    <Header className="flex min-h-control items-center px-2 py-1 text-fg-muted">
                      {crumb.label}
                    </Header>
                  </AriaMenuSection>
                );
              }
              return (
                <MenuItem
                  key={id}
                  id={id}
                  onAction={() => {
                    void router.navigate(linkOptions(link));
                  }}
                >
                  {crumb.label}
                </MenuItem>
              );
            }
            const name = crumb.kind === "org" ? model.org?.name : model.project?.name;
            const kind = crumb.kind;
            return (
              <MenuItem
                key={id}
                id={id}
                onAction={() => {
                  setSwitcher(kind);
                }}
              >
                {name ?? ""}
              </MenuItem>
            );
          })}
        </Menu>
      </MenuTrigger>
      {switcher && model[switcher] && (
        <SwitcherPopover
          kind={switcher}
          name={model[switcher].name}
          currentId={model[switcher].id}
          search={switcher === "org" ? model.searchOrgs : model.searchProjects}
          links={switcher === "org" ? model.orgLinks : model.projectLinks}
          isOpen
          onOpenChange={(isOpen) => {
            if (!isOpen) setSwitcher(null);
          }}
          triggerRef={buttonRef}
        />
      )}
    </span>
  );
}
