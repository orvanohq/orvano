"use client";

import Link from "next/link";
import { Menu } from "lucide-react";
import { useState } from "react";
import { cn } from "cn";

import { Button } from "@/components/ui/button";
import { Separator } from "@/components/ui/separator";
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from "@/components/ui/sheet";

import { backToProjects } from "./nav";
import { NavLinks, type NavLinkProps } from "./nav-links";
import { useRail } from "./use-rail";

/**
 * Below 768px the rail becomes this drawer: a labelled menu button in the top bar opens the same items as a
 * 240px sheet from the left, with labels visible. Radix keeps focus inside and returns it to the button.
 */
export function NavDrawer() {
  const { pathname, kind, items } = useRail();
  const [open, setOpen] = useState(false);
  if (kind === "none") {
    return null;
  }

  const close = () => setOpen(false);
  const DrawerItem = (props: NavLinkProps) => (
    <DrawerLink {...props} onSelect={close} />
  );

  return (
    <Sheet open={open} onOpenChange={setOpen}>
      <SheetTrigger asChild>
        <Button
          variant="ghost"
          size="icon-sm"
          aria-label="Open navigation"
          className="-ml-1 md:hidden"
        >
          <Menu aria-hidden="true" />
        </Button>
      </SheetTrigger>
      <SheetContent side="left" className="gap-0 bg-surface-subtle">
        <SheetHeader className="border-b border-border px-4 py-3">
          <SheetTitle>Navigation</SheetTitle>
          <SheetDescription className="sr-only">
            {kind === "service"
              ? "Go to a page in this project"
              : "Go to a page in this instance"}
          </SheetDescription>
        </SheetHeader>
        <nav
          aria-label={kind === "service" ? "Project" : "Instance"}
          className="flex flex-col gap-1 p-2"
        >
          {kind === "service" && (
            <>
              <DrawerLink
                item={backToProjects}
                href={backToProjects.route}
                current={false}
                onSelect={close}
              />
              <Separator className="my-1" />
            </>
          )}
          <NavLinks
            items={items}
            pathname={pathname}
            carryProject={kind === "service"}
            Item={DrawerItem}
          />
        </nav>
      </SheetContent>
    </Sheet>
  );
}

function DrawerLink({
  item,
  href,
  current,
  onSelect,
}: NavLinkProps & { onSelect: () => void }) {
  const Icon = item.icon;
  return (
    <Link
      href={href}
      onClick={onSelect}
      aria-current={current ? "page" : undefined}
      className={cn(
        "flex h-10 items-center gap-3 rounded-md px-3 text-sm text-ink-muted transition-colors outline-none hover:bg-surface hover:text-ink focus-visible:ring-2 focus-visible:ring-ring",
        current &&
          "bg-brand-subtle font-medium text-brand-ink hover:bg-brand-subtle hover:text-brand-ink",
      )}
    >
      <Icon aria-hidden="true" className="size-5" strokeWidth={1.5} />
      {item.label}
    </Link>
  );
}
