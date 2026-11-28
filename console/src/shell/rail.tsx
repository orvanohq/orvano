"use client";

import Link from "next/link";
import { cn } from "cn";

import { Separator } from "@/components/ui/separator";
import {
  Tooltip,
  TooltipContent,
  TooltipTrigger,
} from "@/components/ui/tooltip";

import { backToProjects } from "./nav";
import { NavLinks, type NavLinkProps } from "./nav-links";
import { useRail } from "./use-rail";

/**
 * The 56px rail: the instance rail on instance pages, the service rail on project pages, nothing on the kit
 * and not found pages. Hidden below 768px, where the top bar's menu button opens the same items in a drawer.
 */
export function Rail() {
  const { pathname, kind, items } = useRail();
  if (kind === "none") {
    return null;
  }

  return (
    <nav
      aria-label={kind === "service" ? "Project" : "Instance"}
      className="sticky top-12 hidden h-[calc(100svh-3rem)] w-14 shrink-0 flex-col items-center gap-1 overflow-y-auto border-r border-border bg-surface-subtle py-2 md:flex"
    >
      {kind === "service" && (
        <>
          <RailItem
            item={backToProjects}
            href={backToProjects.route}
            current={false}
          />
          <Separator className="my-1 w-8" />
        </>
      )}
      <NavLinks
        items={items}
        pathname={pathname}
        carryProject={kind === "service"}
        Item={RailItem}
      />
    </nav>
  );
}

function RailItem({ item, href, current }: NavLinkProps) {
  const Icon = item.icon;
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Link
          href={href}
          aria-label={item.label}
          aria-current={current ? "page" : undefined}
          className={cn(
            "flex size-10 shrink-0 items-center justify-center rounded-md text-ink-muted transition-colors outline-none hover:bg-surface hover:text-ink focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background",
            current &&
              "bg-brand-subtle text-brand-ink hover:bg-brand-subtle hover:text-brand-ink",
          )}
        >
          <Icon aria-hidden="true" className="size-5" strokeWidth={1.5} />
        </Link>
      </TooltipTrigger>
      <TooltipContent side="right" sideOffset={6}>
        {item.label}
      </TooltipContent>
    </Tooltip>
  );
}
