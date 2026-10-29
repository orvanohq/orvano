import { useEffect, useRef, type ReactNode } from "react";
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
import { useGalleryText } from "../pseudo";
import { Row, Scene, Section } from "./scene";

const variants: ButtonVariant[] = ["primary", "secondary", "ghost", "danger"];
const sizes: ButtonSize[] = ["sm", "md", "lg"];
const noPermission = msg`You need the Admin role to delete tables.`;

/** Every foundation primitive in every variant and state (GAL-2). */
export function PrimitivesScene() {
  const g = useGalleryText();
  return (
    <Scene title="Primitives">
      <Section title="Button">
        {variants.map((variant) => (
          <Row key={variant} label={variant}>
            {sizes.map((size) => (
              <Button key={size} variant={variant} size={size}>
                {g(`Create ${size}`)}
              </Button>
            ))}
            <Button variant={variant} unavailableReason={noPermission}>
              {g("Unavailable")}
            </Button>
            <Button variant={variant} isDisabled>
              {g("Disabled")}
            </Button>
          </Row>
        ))}
      </Section>

      <Section title="Button, forced states">
        <p className="text-fg-muted">
          {g("Each button holds one interaction state, so the recipes can be compared at rest.")}
        </p>
        {variants.map((variant) => (
          <Row key={variant} label={variant}>
            {states.map((state) => (
              <ForcedState key={state} state={state}>
                <Button variant={variant}>{g(stateLabels[state])}</Button>
              </ForcedState>
            ))}
          </Row>
        ))}
      </Section>

      <Section title="IconButton">
        <Row label="ghost">
          <IconButton icon={CloseIcon} aria-label={g("Close")} size="sm" />
          <IconButton icon={CloseIcon} aria-label={g("Close")} />
          <IconButton icon={CloseIcon} aria-label={g("Close")} unavailableReason={noPermission} />
        </Row>
        <Row label="secondary">
          <IconButton
            icon={ChevronStartIcon}
            aria-label={g("Previous")}
            variant="secondary"
            flipInRtl
          />
          <IconButton icon={ChevronEndIcon} aria-label={g("Next")} variant="secondary" flipInRtl />
        </Row>
      </Section>

      <Section title="Link">
        <p>
          {g("An inline link to")}{" "}
          <Link to="/" variant="inline">
            {g("the gallery index")}
          </Link>{" "}
          {g("in running text.")}
        </p>
        <Row label="nav">
          <Link to="/" variant="nav">
            {g("Gallery index")}
          </Link>
        </Row>
      </Section>

      <Section title="Tooltip">
        <Row label="on a button">
          <Tooltip content={g("Saves the row and closes the sheet")}>
            <Button>{g("Hover or focus me")}</Button>
          </Tooltip>
        </Row>
      </Section>

      <Section title="Icon">
        <Row label="sizes">
          <Icon icon={ServiceDatabasesIcon} />
          <Icon icon={ServiceDatabasesIcon} size={20} />
          <Icon icon={ServiceDatabasesIcon} size={24} label={g("Databases")} />
        </Row>
        <Row label="flipInRtl">
          <Icon icon={ChevronStartIcon} flipInRtl label={g("Back")} />
          <Icon icon={ChevronEndIcon} flipInRtl label={g("Forward")} />
        </Row>
      </Section>

      <Section title="Menu">
        <Row label="sections">
          <MenuTrigger trigger={<Button>{g("Table actions")}</Button>}>
            <Menu aria-label={g("Table actions")}>
              <MenuSection title={g("Sort")} selectedKey="name">
                <MenuItem id="name">{g("By name")}</MenuItem>
                <MenuItem id="created">{g("By creation date")}</MenuItem>
              </MenuSection>
              <MenuSeparator />
              <MenuItem id="rename">{g("Rename table")}</MenuItem>
              <MenuItem id="export" unavailableReason={noPermission}>
                {g("Export rows")}
              </MenuItem>
              <MenuItem id="delete" tone="danger" icon={SignOutIcon}>
                {g("Delete table")}
              </MenuItem>
            </Menu>
          </MenuTrigger>
        </Row>
      </Section>

      <Section title="Tabs">
        <Tabs>
          <TabList aria-label={g("SDK")}>
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
            {g("Open the Orvano docs")}
          </ExternalLink>
        </p>
      </Section>

      <Section title="VisuallyHidden">
        <p>
          {g("There is hidden text after this sentence.")}
          <VisuallyHidden> {g("Screen readers read this part.")}</VisuallyHidden>
        </p>
      </Section>
    </Scene>
  );
}

type ForcedStateName = "hovered" | "pressed" | "focus-visible";
const states: readonly ForcedStateName[] = ["hovered", "pressed", "focus-visible"];
const stateLabels: Record<ForcedStateName, string> = {
  hovered: "Hovered",
  pressed: "Pressed",
  "focus-visible": "Focused",
};

/**
 * Holds the button inside it in one React Aria interaction state by setting the `data-*` attribute its styles read
 * (design.md _Button recipes_). React Aria never renders the attribute while the state is off, so React leaves it.
 */
function ForcedState({ state, children }: { state: ForcedStateName; children: ReactNode }) {
  const ref = useRef<HTMLSpanElement>(null);
  useEffect(() => {
    ref.current?.querySelector("button")?.setAttribute(`data-${state}`, "true");
  }, [state]);
  return (
    <span ref={ref} className="contents">
      {children}
    </span>
  );
}
