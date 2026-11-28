import { EmptyState } from "@/components/empty-state";
import { PageHeader } from "@/components/page-header";

import type { NavItem } from "./nav";

/** What every rail destination shows until its feature builds the real page (spec 0003, AC-10). */
export function StubPage({ item }: { item: NavItem }) {
  return (
    <div className="mx-auto flex w-full max-w-7xl flex-col gap-6">
      <PageHeader title={item.title} description={item.description} />
      <EmptyState icon={item.icon} title={`${item.title} isn't built yet`}>
        This page arrives in milestone {item.milestone}.
      </EmptyState>
    </div>
  );
}
