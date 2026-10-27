import { useRef, useState } from "react";
import { useLingui } from "@lingui/react/macro";
import { useRouter } from "@tanstack/react-router";
import { Breadcrumb, Breadcrumbs as AriaBreadcrumbs } from "react-aria-components";
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

/**
 * Spec 0015 AC-9: the `nav` labeled "Breadcrumb" around React Aria's `Breadcrumbs` list. Org and project crumbs
 * show their names from the model and open the switchers; page crumbs are links, except the last, which is
 * `aria-current="page"`.
 */
export function Breadcrumbs({ model, crumbs, collapsed = false }: BreadcrumbsProps) {
  const { t } = useLingui();
  const hidden = collapsed && crumbs.length > 2 ? crumbs.slice(1, -1) : [];
  const shown =
    hidden.length > 0
      ? [{ crumb: crumbs[0], index: 0 }, null, { crumb: crumbs.at(-1), index: crumbs.length - 1 }]
      : crumbs.map((crumb, index) => ({ crumb, index }));

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
    return <OrgSwitcher name={model.org.name} search={model.searchOrgs} links={model.orgLinks} />;
  }
  if (crumb.kind === "project" && model.project) {
    return (
      <ProjectSwitcher
        name={model.project.name}
        search={model.searchProjects}
        links={model.projectLinks}
      />
    );
  }
  if (crumb.kind !== "page") return null;
  if (isCurrent || !crumb.link) {
    return (
      <span
        aria-current={isCurrent ? "page" : undefined}
        className={isCurrent ? "truncate font-medium text-fg" : "truncate text-fg-muted"}
      >
        {crumb.label}
      </span>
    );
  }
  return (
    <span className="min-w-0 truncate text-fg-muted">
      {/* Exact, so a crumb to an ancestor route isn't marked current too: only the last crumb is (AC-9). */}
      <Link variant="nav" {...linkOptions(crumb.link)} activeOptions={{ exact: true }}>
        {crumb.label}
      </Link>
    </span>
  );
}

/** The middle crumbs in a menu. Org and project items open their switcher, anchored to the menu's button. */
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
              return (
                <MenuItem
                  key={id}
                  id={id}
                  onAction={
                    link
                      ? () => {
                          void router.navigate(linkOptions(link));
                        }
                      : undefined
                  }
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
