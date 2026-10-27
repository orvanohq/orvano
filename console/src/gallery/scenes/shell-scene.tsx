import {
  AppShell,
  Banner,
  Button,
  PageHeader,
  ResourceLayout,
  ResourceList,
  RouteTabs,
  type ShellScope,
} from "@/ui";
import { crumbs, shellModel, tables, tableTabs } from "../fixtures";

/** The shell in one scope, on placeholder data (GAL-2). Databases shows the resource list and a table's tabs. */
export function ShellScene({
  scope,
  page,
  withResourceList = false,
  tableId,
  tab,
  withBanner = false,
}: {
  scope: ShellScope;
  page: string;
  withResourceList?: boolean;
  tableId?: string;
  tab?: string;
  withBanner?: boolean;
}) {
  const title = page.charAt(0).toUpperCase() + page.slice(1);
  return (
    <AppShell
      model={shellModel(scope)}
      crumbs={crumbs(scope, title, tableId)}
      banner={
        withBanner ? (
          <Banner tone="warning" action={<Button size="sm">Upgrade plan</Button>}>
            Storefront is at 90% of its database storage.
          </Banner>
        ) : undefined
      }
      resourceList={
        withResourceList ? (
          <ResourceLayout service="databases" label="Tables" resizeLabel="Resize table list">
            <ResourceList
              items={tables}
              filterLabel="Filter tables"
              emptyMessage="No tables yet"
              createLabel="Create table"
              onCreate={() => undefined}
            />
          </ResourceLayout>
        ) : undefined
      }
    >
      <div className="flex flex-col gap-6">
        {tableId ? (
          <>
            <PageHeader
              title={tableId}
              description="A table in the Storefront database."
              actions={<Button variant="primary">Insert row</Button>}
            />
            <RouteTabs aria-label="Table" tabs={tableTabs(tableId)} />
            <p className="text-fg-muted">
              The {tab ?? "rows"} tab. Grids arrive with the rows feature.
            </p>
          </>
        ) : (
          <>
            <PageHeader title={title} description={`Placeholder content for the ${scope} scope.`} />
            {withResourceList && <p className="text-fg-muted">Open a table from the list.</p>}
          </>
        )}
      </div>
    </AppShell>
  );
}
