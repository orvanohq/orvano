"use client";

import { Plus } from "lucide-react";
import { useState } from "react";

import { CodeBlock } from "@/components/code-block";
import { PageHeader } from "@/components/page-header";
import { StatusDot } from "@/components/status-dot";
import { TypeToConfirm } from "@/components/type-to-confirm";
import { Badge, type BadgeVariant } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { kitTitle } from "@/shell/nav";

import { BucketForm } from "./bucket-form";
import { DeliveriesTable } from "./deliveries-table";
import { KitSection } from "./kit-section";

const buttonVariants = [
  { variant: "default", name: "Primary" },
  { variant: "secondary", name: "Secondary" },
  { variant: "ghost", name: "Ghost" },
  { variant: "destructive", name: "Destructive" },
] as const;

const badges: { variant: BadgeVariant; label: string }[] = [
  { variant: "neutral", label: "Draft" },
  { variant: "brand", label: "Selected" },
  { variant: "success", label: "Deployed" },
  { variant: "warning", label: "Pending approval" },
  { variant: "danger", label: "Failed" },
];

/**
 * The kit page: one section per base component, every state, sample data only. It is the one page with
 * more than one primary button, because it exists to show every variant.
 */
export function Kit() {
  const [deleted, setDeleted] = useState(false);

  return (
    <div className="mx-auto flex w-full max-w-7xl flex-col gap-8">
      <PageHeader
        title={kitTitle}
        description="Every base component and state, for testing and reference. Switch the theme from the top bar."
      />

      <KitSection
        id="kit-buttons"
        title="Buttons"
        description="Primary, secondary, ghost and destructive, at 32px and 36px, and a disabled primary."
      >
        <div className="flex flex-col gap-4">
          {(["sm", "default"] as const).map((size) => (
            <div key={size} className="flex flex-wrap items-center gap-3">
              <span className="w-12 text-xs text-ink-muted">
                {size === "sm" ? "32px" : "36px"}
              </span>
              {buttonVariants.map(({ variant, name }) => (
                <Button
                  key={variant}
                  variant={variant}
                  size={size}
                  aria-description={`${name} button`}
                >
                  {variant === "default" && (
                    <Plus aria-hidden="true" data-icon="inline-start" />
                  )}
                  Create bucket
                </Button>
              ))}
            </div>
          ))}
          <div className="flex flex-wrap items-center gap-3">
            <span className="w-12 text-xs text-ink-muted">Disabled</span>
            <Button disabled>Create bucket</Button>
          </div>
        </div>
      </KitSection>

      <KitSection
        id="kit-badges"
        title="Badges"
        description="A subtle background with text that passes AA, and an optional dot in the strong colour."
      >
        <div className="flex flex-col gap-3">
          {[false, true].map((dot) => (
            <div
              key={String(dot)}
              className="flex flex-wrap items-center gap-2"
            >
              {badges.map((badge) => (
                <Badge key={badge.variant} variant={badge.variant} dot={dot}>
                  {badge.label}
                </Badge>
              ))}
            </div>
          ))}
        </div>
      </KitSection>

      <KitSection
        id="kit-status"
        title="Status dots"
        description="Status is never colour alone: the dot is always followed by its word."
      >
        <div className="flex flex-wrap items-center gap-6">
          <StatusDot status="success">Healthy</StatusDot>
          <StatusDot status="warning">Degraded</StatusDot>
          <StatusDot status="danger">Down</StatusDot>
        </div>
      </KitSection>

      <KitSection
        id="kit-table"
        title="Table"
        description="36px rows, a sticky header, right aligned numbers and mono IDs. Open a row to see its side sheet."
      >
        <DeliveriesTable />
      </KitSection>

      <KitSection
        id="kit-form"
        title="Form"
        description="Labels above fields, help text, an inline error that is announced, a select and a checkbox."
      >
        <BucketForm />
      </KitSection>

      <KitSection
        id="kit-confirm"
        title="Destructive confirmation"
        description="The danger button enables only when the resource name is typed."
      >
        <div className="flex max-w-md flex-col gap-4">
          <TypeToConfirm
            resourceName="recipes-dev"
            actionLabel="Delete bucket"
            onConfirm={() => setDeleted(true)}
          >
            <div className="flex flex-col gap-1">
              <h3 className="text-h3 text-ink">Delete bucket</h3>
              <p className="text-body text-ink-muted">
                Deletes the bucket{" "}
                <span className="font-mono text-mono">recipes-dev</span> and
                every file in it.
              </p>
            </div>
          </TypeToConfirm>
          <p role="status" className="text-body text-ink">
            {deleted ? "Nothing was deleted: this is the kit page." : ""}
          </p>
        </div>
      </KitSection>

      <KitSection
        id="kit-code"
        title="Code block"
        description="Mono on surface subtle, a language label and a copy button that announces the result."
      >
        <div className="flex flex-col gap-4">
          <CodeBlock
            language="sql"
            code="select id, title from recipes where author_id = $1 limit 20;"
          />
          <CodeBlock language="bash" code="orvano db migrate up" />
        </div>
      </KitSection>
    </div>
  );
}
