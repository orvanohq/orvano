import type { ReactNode } from "react";
import { msg } from "@lingui/core/macro";
import {
  Avatar,
  Button,
  ChevronEndIcon,
  ChevronStartIcon,
  CloseIcon,
  ExternalLink,
  Icon,
  IconButton,
  Kbd,
  Link,
  Menu,
  MenuItem,
  MenuSection,
  MenuSeparator,
  MenuTrigger,
  ServiceDatabasesIcon,
  SignOutIcon,
  Tab,
  TabList,
  TabPanel,
  Tabs,
  Tooltip,
  VisuallyHidden,
  type ButtonSize,
  type ButtonVariant,
} from "@/ui";

const variants: ButtonVariant[] = ["primary", "secondary", "ghost", "danger"];
const sizes: ButtonSize[] = ["sm", "md", "lg"];
const noPermission = msg`You need the Admin role to delete tables.`;

/** Every foundation primitive in every variant and state (GAL-2). */
export function PrimitivesScene() {
  return (
    <main className="mx-auto flex max-w-content flex-col gap-8 p-6">
      <h1 className="text-xl font-semibold">Primitives</h1>

      <Section title="Button">
        {variants.map((variant) => (
          <Row key={variant} label={variant}>
            {sizes.map((size) => (
              <Button key={size} variant={variant} size={size}>
                Create {size}
              </Button>
            ))}
            <Button variant={variant} unavailableReason={noPermission}>
              Unavailable
            </Button>
            <Button variant={variant} isDisabled>
              Disabled
            </Button>
          </Row>
        ))}
      </Section>

      <Section title="IconButton">
        <Row label="ghost">
          <IconButton icon={CloseIcon} aria-label="Close" size="sm" />
          <IconButton icon={CloseIcon} aria-label="Close" />
          <IconButton icon={CloseIcon} aria-label="Close" unavailableReason={noPermission} />
        </Row>
        <Row label="secondary">
          <IconButton icon={ChevronStartIcon} aria-label="Previous" variant="secondary" flipInRtl />
          <IconButton icon={ChevronEndIcon} aria-label="Next" variant="secondary" flipInRtl />
        </Row>
      </Section>

      <Section title="Link">
        <p>
          An inline link to{" "}
          <Link to="/" variant="inline">
            the gallery index
          </Link>{" "}
          in running text.
        </p>
        <Row label="nav">
          <Link to="/" variant="nav">
            Gallery index
          </Link>
        </Row>
      </Section>

      <Section title="Tooltip">
        <Row label="on a button">
          <Tooltip content="Saves the row and closes the sheet">
            <Button>Hover or focus me</Button>
          </Tooltip>
        </Row>
      </Section>

      <Section title="Icon">
        <Row label="sizes">
          <Icon icon={ServiceDatabasesIcon} />
          <Icon icon={ServiceDatabasesIcon} size={20} />
          <Icon icon={ServiceDatabasesIcon} size={24} label="Databases" />
        </Row>
        <Row label="flipInRtl">
          <Icon icon={ChevronStartIcon} flipInRtl label="Back" />
          <Icon icon={ChevronEndIcon} flipInRtl label="Forward" />
        </Row>
      </Section>

      <Section title="Menu">
        <Row label="sections">
          <MenuTrigger trigger={<Button>Table actions</Button>}>
            <Menu aria-label="Table actions">
              <MenuSection title="Sort" selectedKey="name">
                <MenuItem id="name">By name</MenuItem>
                <MenuItem id="created">By creation date</MenuItem>
              </MenuSection>
              <MenuSeparator />
              <MenuItem id="rename">Rename table</MenuItem>
              <MenuItem id="export" unavailableReason={noPermission}>
                Export rows
              </MenuItem>
              <MenuItem id="delete" tone="danger" icon={SignOutIcon}>
                Delete table
              </MenuItem>
            </Menu>
          </MenuTrigger>
        </Row>
      </Section>

      <Section title="Tabs">
        <Tabs>
          <TabList aria-label="SDK">
            <Tab id="js">JavaScript</Tab>
            <Tab id="dart">Dart</Tab>
            <Tab id="dotnet">.NET</Tab>
          </TabList>
          <TabPanel id="js">npm install @orvano/sdk</TabPanel>
          <TabPanel id="dart">dart pub add orvano</TabPanel>
          <TabPanel id="dotnet">dotnet add package Orvano</TabPanel>
        </Tabs>
      </Section>

      <Section title="Avatar">
        <Row label="sizes">
          <Avatar name="Ada Lovelace" email="ada@example.com" size="sm" />
          <Avatar name="Ada Lovelace" email="ada@example.com" />
          <Avatar name="" email="grace@example.com" />
          <Avatar name="Émile" email="emile@example.com" />
        </Row>
      </Section>

      <Section title="Kbd">
        <Row label="Mod+K">
          <Kbd keys="Mod+K" />
        </Row>
        <Row label="?">
          <Kbd keys="?" />
        </Row>
      </Section>

      <Section title="ExternalLink">
        <p>
          <ExternalLink href="https://github.com/orvanohq/orvano#readme">
            Open the Orvano docs
          </ExternalLink>
        </p>
      </Section>

      <Section title="VisuallyHidden">
        <p>
          There is hidden text after this sentence.
          <VisuallyHidden> Screen readers read this part.</VisuallyHidden>
        </p>
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

function Row({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-wrap items-center gap-3">
      <span className="w-24 shrink-0 font-mono text-sm text-fg-muted">{label}</span>
      {children}
    </div>
  );
}
