import { useState, type ReactNode } from "react";
import { Cell, Column, Row, Table, TableBody, TableHeader, type SortDescriptor } from "@/ui/table";
import {
  Badge,
  Button,
  CodeSnippet,
  CopyField,
  DangerZone,
  DangerZoneItem,
  EmptyState,
  RelativeTime,
  SecretReveal,
  SettingsCard,
} from "@/ui";

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
  const [sort, setSort] = useState<SortDescriptor>({ column: "name", direction: "ascending" });
  const sorted = keys.toSorted((a, b) => {
    const order = a.name.localeCompare(b.name);
    return sort.direction === "ascending" ? order : -order;
  });
  return (
    <main className="mx-auto flex max-w-content flex-col gap-8 p-6">
      <h1 className="text-xl font-semibold">Data display</h1>

      <Section title="Table, sortable">
        <Table aria-label="API keys" sortDescriptor={sort} onSortChange={setSort}>
          <TableHeader>
            <Column id="name" isRowHeader allowsSorting>
              Name
            </Column>
            <Column id="key">Key</Column>
            <Column id="created">Created</Column>
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
        <Table aria-label="Members" selectionMode="multiple">
          <TableHeader>
            <Column id="email" isRowHeader>
              Email
            </Column>
            <Column id="role">Role</Column>
          </TableHeader>
          <TableBody>
            <Row id="ada">
              <Cell>ada@example.com</Cell>
              <Cell>
                <Badge tone="info">Owner</Badge>
              </Cell>
            </Row>
            <Row id="lin">
              <Cell>lin@example.com</Cell>
              <Cell>
                <Badge>Developer</Badge>
              </Cell>
            </Row>
          </TableBody>
        </Table>
      </Section>

      <Section title="Table, empty">
        <Table aria-label="Webhooks">
          <TableHeader>
            <Column id="url" isRowHeader>
              URL
            </Column>
            <Column id="events">Events</Column>
          </TableHeader>
          <TableBody
            emptyState={
              <EmptyState
                kind="first-use"
                title="No webhooks yet"
                description="Webhooks tell your server when something happens."
                action={<Button variant="primary">Create webhook</Button>}
              />
            }
          >
            {[]}
          </TableBody>
        </Table>
      </Section>

      <Section title="SettingsCard and DangerZone">
        <SettingsCard
          title="General"
          description="The project's name and region."
          footer={<Button variant="primary">Update</Button>}
        >
          <CopyField label="Project ID" value="prj_01JB8Z4Q7W2N" />
        </SettingsCard>
        <DangerZone>
          <DangerZoneItem
            title="Transfer project"
            description="Move Storefront to another organization."
            action={<Button>Transfer project</Button>}
          />
          <DangerZoneItem
            title="Delete project"
            description="Deletes the project and everything in it."
            action={<Button variant="danger">Delete project</Button>}
          />
        </DangerZone>
      </Section>

      <Section title="CopyField">
        <CopyField label="Endpoint" value="https://api.example.com/v1" />
      </Section>

      <Section title="SecretReveal">
        <SecretReveal
          label="API key"
          secret="ok_live_7f3a2c9d0e1b4a5f6c7d8e9f0a1b91c2"
          masked="ok_live_7f3a…91c2"
        />
      </Section>

      <Section title="CodeSnippet">
        <CodeSnippet label="Install command" code="npm install @orvano/sdk" />
      </Section>

      <Section title="RelativeTime">
        <ul className="flex flex-col gap-1">
          <li>
            Just now: <RelativeTime datetime={new Date(now)} />
          </li>
          <li>
            Minutes ago: <RelativeTime datetime={new Date(now - 3 * minute)} />
          </li>
          <li>
            In the future: <RelativeTime datetime={new Date(now + 5 * minute)} />
          </li>
          <li>
            Days ago: <RelativeTime datetime={new Date(now - 2 * day)} />
          </li>
          <li>
            Past 7 days, a date: <RelativeTime datetime={new Date(now - 40 * day)} />
          </li>
        </ul>
      </Section>
    </main>
  );
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="flex flex-col gap-3">
      <h2 className="text-lg font-semibold">{title}</h2>
      {children}
    </section>
  );
}
