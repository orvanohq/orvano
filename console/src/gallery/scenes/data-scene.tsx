import { useState } from "react";
import { Cell, Column, Row, Table, TableBody, TableHeader, type SortDescriptor } from "@/ui/table";
import { Button, CodeSnippet, CopyField, EmptyState, RelativeTime, SecretReveal } from "@/ui";
import { useGalleryText } from "../pseudo";
import { Scene, Section } from "./scene";
import { MembersTable, NameSettingsCard, ResourceDangerZone } from "./settings-blocks";

// Spec 0015 GAL-2: data display in every state, on placeholder data (milestone 4, step 19). The gallery imports the
// lazy table directly; screens load it lazily (PRM-10).

const minute = 60_000;
const day = 24 * 60 * minute;
const now = Date.now();

const keys = [
  {
    id: "ci",
    name: "CI deploys",
    prefix: "ok_live_7f3a…91c2",
    created: new Date(now - 3 * minute),
  },
  {
    id: "mobile",
    name: "Mobile app",
    prefix: "ok_live_2b8d…04e7",
    created: new Date(now - 2 * day),
  },
  { id: "old", name: "Old server", prefix: "ok_live_c41e…77fa", created: new Date(now - 40 * day) },
];

export function DataScene() {
  const g = useGalleryText();
  const [sort, setSort] = useState<SortDescriptor>({ column: "name", direction: "ascending" });
  const sorted = keys.toSorted((a, b) => {
    const order = a.name.localeCompare(b.name);
    return sort.direction === "ascending" ? order : -order;
  });
  return (
    <Scene title="Data display">
      <Section title="Table, sortable">
        <Table aria-label={g("API keys")} sortDescriptor={sort} onSortChange={setSort}>
          <TableHeader>
            <Column id="name" isRowHeader allowsSorting>
              {g("Name")}
            </Column>
            <Column id="key">{g("Key")}</Column>
            <Column id="created">{g("Created")}</Column>
          </TableHeader>
          <TableBody>
            {sorted.map((key) => (
              <Row key={key.id} id={key.id}>
                <Cell>{key.name}</Cell>
                <Cell>
                  <span className="font-mono">{key.prefix}</span>
                </Cell>
                <Cell>
                  <RelativeTime datetime={key.created} />
                </Cell>
              </Row>
            ))}
          </TableBody>
        </Table>
      </Section>

      <Section title="Table, selectable">
        <MembersTable />
      </Section>

      <Section title="Table, empty">
        <Table aria-label={g("Webhooks")}>
          <TableHeader>
            <Column id="url" isRowHeader>
              {g("URL")}
            </Column>
            <Column id="events">{g("Events")}</Column>
          </TableHeader>
          <TableBody
            emptyState={
              <EmptyState
                kind="first-use"
                title={g("No webhooks yet")}
                description={g("Webhooks tell your server when something happens.")}
                action={<Button variant="primary">{g("Create webhook")}</Button>}
              />
            }
          >
            {[]}
          </TableBody>
        </Table>
      </Section>

      <Section title="SettingsCard and DangerZone">
        <NameSettingsCard
          title="General"
          description="The project's name, and the ID the SDKs use."
          nameLabel="Project name"
          name="Storefront"
          idLabel="Project ID"
          id="prj_01JB8Z4Q7W2N"
        />
        <ResourceDangerZone
          transfer={{
            title: "Transfer project",
            description: "Move Storefront to another organization.",
          }}
          remove={{
            title: "Delete project",
            description: "Deletes the project and everything in it.",
          }}
        />
      </Section>

      <Section title="CopyField">
        <CopyField label={g("Endpoint")} value="https://api.example.com/v1" />
      </Section>

      <Section title="SecretReveal">
        <SecretReveal
          label={g("API key")}
          secret="ok_live_7f3a2c9d0e1b4a5f6c7d8e9f0a1b91c2"
          masked="ok_live_7f3a…91c2"
        />
      </Section>

      <Section title="CodeSnippet">
        <CodeSnippet label={g("Install command")} code="npm install @orvano/sdk" />
      </Section>

      <Section title="RelativeTime">
        <ul className="flex flex-col gap-1">
          <li>
            {g("Just now:")} <RelativeTime datetime={new Date(now)} />
          </li>
          <li>
            {g("Minutes ago:")} <RelativeTime datetime={new Date(now - 3 * minute)} />
          </li>
          <li>
            {g("In the future:")} <RelativeTime datetime={new Date(now + 5 * minute)} />
          </li>
          <li>
            {g("Days ago:")} <RelativeTime datetime={new Date(now - 2 * day)} />
          </li>
          <li>
            {g("Past 7 days, a date:")} <RelativeTime datetime={new Date(now - 40 * day)} />
          </li>
        </ul>
      </Section>
    </Scene>
  );
}
