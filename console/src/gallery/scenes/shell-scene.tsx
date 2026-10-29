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
import { crumbs, org, shellModel, tables, tableTabs, user } from "../fixtures";
import { useGalleryText } from "../pseudo";
import { useScenarioData } from "../scenario";
import { useSettings } from "../settings";
import { MembersTable, NameSettingsCard, ResourceDangerZone } from "./settings-blocks";

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
  const g = useGalleryText();
  const { scenario } = useSettings();
  const items = useScenarioData(tables, []);
  const title = g(page.charAt(0).toUpperCase() + page.slice(1));
  return (
    <AppShell
      model={shellModel(scope, scenario)}
      crumbs={crumbs(scope, title, tableId)}
      banner={
        withBanner ? (
          <Banner tone="warning" action={<Button size="sm">{g("Upgrade plan")}</Button>}>
            {g("Storefront is at 90% of its database storage.")}
          </Banner>
        ) : undefined
      }
      resourceList={
        withResourceList ? (
          <ResourceLayout
            service="databases"
            label={g("Tables")}
            resizeLabel={g("Resize table list")}
          >
            <ResourceList
              items={items}
              filterLabel={g("Filter tables")}
              emptyMessage={g("No tables yet")}
              createLabel={g("Create table")}
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
              description={g("A table in the Storefront database.")}
              actions={<Button variant="primary">{g("Insert row")}</Button>}
            />
            <RouteTabs aria-label={g("Table")} tabs={tableTabs(tableId, g)} />
            <p className="text-fg-muted">
              {g(`The ${tab ?? "rows"} tab. Grids arrive with the rows feature.`)}
            </p>
          </>
        ) : (
          <>
            <PageHeader title={title} description={g(descriptions[page] ?? placeholder)} />
            {withResourceList && (
              <p className="text-fg-muted">{g("Open a table from the list.")}</p>
            )}
            <PageContent page={page} />
          </>
        )}
      </div>
    </AppShell>
  );
}

const placeholder = "Placeholder content for this page.";

const descriptions: Readonly<Record<string, string>> = {
  Members: "People who can open Acme's projects, and their roles.",
  "Organization settings": "Acme's name, and what happens to it.",
  profile: "Your name and email address.",
  security: "How you sign in.",
  preferences: "How the console looks for you.",
};

/** G-28: the org and account pages hold real settings content, so their spacing is seen inside the shell. */
function PageContent({ page }: { page: string }) {
  switch (page) {
    case "Members":
      return <MembersTable />;
    case "Organization settings":
      return (
        <>
          <NameSettingsCard
            title="General"
            description="The organization's name, and the ID the API uses."
            nameLabel="Organization name"
            name={org.name}
            idLabel="Organization ID"
            id="org_01JB8Z4Q7W2N"
          />
          <ResourceDangerZone
            remove={{
              title: "Delete organization",
              description: "Deletes Acme and every project in it.",
            }}
          />
        </>
      );
    case "profile":
      return (
        <>
          <NameSettingsCard
            title="Profile"
            description="How your teammates see you."
            nameLabel="Name"
            name={user.name}
            idLabel="User ID"
            id={user.id}
          />
          <ResourceDangerZone
            remove={{
              title: "Delete account",
              description: "Deletes your account. Organizations you own need a new owner first.",
            }}
          />
        </>
      );
    default:
      return null;
  }
}
