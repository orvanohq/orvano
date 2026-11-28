"use client";

import { Suspense, type ComponentType, type ReactNode } from "react";

import { isCurrent, type NavItem } from "./nav";
import { projectHref, useProjectId } from "./project-id";

export type NavLinkProps = {
  item: Pick<NavItem, "label" | "icon" | "route">;
  href: string;
  current: boolean;
};

/**
 * Renders every item through `Item`. On the service rail the links carry `?project=` once the id is known;
 * before that (the prerendered HTML and the first paint) they render without it. Only this list waits on
 * the search params, so the rail itself is in the prerendered HTML.
 */
export function NavLinks({
  items,
  pathname,
  carryProject,
  Item,
}: {
  items: readonly NavItem[];
  pathname: string;
  carryProject: boolean;
  Item: ComponentType<NavLinkProps>;
}) {
  const plain = (
    <>
      {items.map((item) => (
        <Item
          key={item.route}
          item={item}
          href={item.route}
          current={isCurrent(item, pathname)}
        />
      ))}
    </>
  );

  if (!carryProject) {
    return plain;
  }
  return (
    <Suspense fallback={plain}>
      <ProjectNavLinks
        items={items}
        pathname={pathname}
        Item={Item}
        fallback={plain}
      />
    </Suspense>
  );
}

function ProjectNavLinks({
  items,
  pathname,
  Item,
  fallback,
}: {
  items: readonly NavItem[];
  pathname: string;
  Item: ComponentType<NavLinkProps>;
  fallback: ReactNode;
}) {
  const project = useProjectId();
  if (project.status !== "valid") {
    return fallback;
  }
  return (
    <>
      {items.map((item) => (
        <Item
          key={item.route}
          item={item}
          href={projectHref(item.route, project.id)}
          current={isCurrent(item, pathname)}
        />
      ))}
    </>
  );
}
