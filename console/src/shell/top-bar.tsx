"use client";

import Link from "next/link";
import { Suspense } from "react";

import { InstanceAddress } from "./instance-address";
import { NavDrawer } from "./nav-drawer";
import { useProjectId } from "./project-id";
import { ThemeMenu } from "./theme-menu";
import { useRail } from "./use-rail";

/**
 * The 48px top bar: the menu button (below 768px), the placeholder mark and "Orvano", the instance address,
 * the project id on project pages, and the theme menu. Later features add, on the right and in this order:
 * the approvals badge (75), the command palette trigger (88), then the user menu (12) after the theme menu.
 */
export function TopBar() {
  const { kind } = useRail();

  return (
    <header className="sticky top-0 z-20 flex h-12 shrink-0 items-center gap-3 border-b border-border bg-surface px-4">
      <NavDrawer />
      <Link
        href="/"
        className="flex items-center gap-2 rounded-sm text-ink outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
      >
        <span aria-hidden="true" className="size-2.5 rounded-sm bg-brand" />
        <span className="text-body font-semibold">Orvano</span>
      </Link>
      {/* Below 640px the project id gets the room; the instance address returns on wider screens. */}
      <span className="hidden min-w-0 items-center gap-3 sm:flex">
        <Separator />
        <InstanceAddress />
      </span>
      {kind === "service" && (
        <Suspense fallback={null}>
          <ProjectCrumb />
        </Suspense>
      )}
      <div className="ml-auto flex items-center gap-1">
        <ThemeMenu />
      </div>
    </header>
  );
}

function Separator() {
  return (
    <span aria-hidden="true" className="text-border-strong select-none">
      /
    </span>
  );
}

function ProjectCrumb() {
  const project = useProjectId();
  if (project.status !== "valid") {
    return null;
  }
  return (
    <>
      <Separator />
      <span className="truncate font-mono text-mono-small text-ink">
        {project.id}
      </span>
    </>
  );
}
