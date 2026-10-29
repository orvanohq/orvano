import { useState } from "react";
import {
  Badge,
  Banner,
  Button,
  ContentSkeleton,
  EmptyState,
  ErrorState,
  GridSkeleton,
  InlineError,
  notify,
  Progress,
  Skeleton,
  Spinner,
  StatusChip,
  useDelayedPending,
  type JobStatus,
  type Tone,
} from "@/ui";
import { useGalleryText } from "../pseudo";
import { Scene, Section } from "./scene";

// Spec 0015 GAL-2: feedback and states in every variant, on placeholder data (milestone 4, steps 17 and 18).

const tones: Tone[] = ["neutral", "info", "success", "warning", "danger"];
const statuses: JobStatus[] = ["queued", "running", "ready", "failed", "cancelled"];

/** One realistic message per tone (design.md _Writing in the UI_), each with the action it would offer. */
const banners: readonly { tone: Tone; title: string; message: string; action: string }[] = [
  {
    tone: "neutral",
    title: "Maintenance on Sunday",
    message: "The instance restarts on Sunday at 02:00 UTC, for about five minutes.",
    action: "View schedule",
  },
  {
    tone: "info",
    title: "A new SDK version",
    message: "Version 2.4 of the JavaScript SDK adds realtime presence.",
    action: "Read the changelog",
  },
  {
    tone: "success",
    title: "Backup finished",
    message: "Last night's backup of Storefront finished at 02:14.",
    action: "View backups",
  },
  {
    tone: "warning",
    title: "Storage almost full",
    message: "Storefront is at 90% of its database storage.",
    action: "Upgrade plan",
  },
  {
    tone: "danger",
    title: "Storage is full",
    message: "Storefront can't take new rows until you free space or upgrade.",
    action: "Upgrade plan",
  },
];

const serverError = { kind: "server", requestId: "req_01JB8Z4Q7W2N" };

export function FeedbackScene() {
  const g = useGalleryText();
  return (
    <Scene title="Feedback and states">
      <Section title="notify and toasts">
        <p className="text-fg-muted">
          {g(
            "Three show at once, newest on top. Success closes after 6 seconds; errors stay until you close them, and the region never covers the focused element. Raise the same one twice to see the count.",
          )}
        </p>
        <div className="flex flex-wrap gap-2">
          <Button
            onPress={() => {
              notify({ tone: "success", message: g("Table created"), source: g("Databases") });
            }}
          >
            {g("Success")}
          </Button>
          <Button
            onPress={() => {
              notify({ tone: "info", message: g("Backup started"), source: g("Backups") });
            }}
          >
            {g("Info")}
          </Button>
          <Button
            onPress={() => {
              notify({ tone: "error", message: g("Deploy failed"), source: g("Functions") });
            }}
          >
            {g("Error")}
          </Button>
        </div>
      </Section>

      <Section title="Banner">
        {banners.map((banner) => (
          <Banner key={banner.tone} tone={banner.tone} title={g(banner.title)}>
            {g(banner.message)}
          </Banner>
        ))}
      </Section>

      <Section title="Banner, with an action">
        {banners.map((banner) => (
          <Banner
            key={banner.tone}
            tone={banner.tone}
            title={g(banner.title)}
            action={<Button size="sm">{g(banner.action)}</Button>}
          >
            {g(banner.message)}
          </Banner>
        ))}
      </Section>

      <Section title="InlineError">
        <InlineError
          error={serverError}
          title={g("Couldn't save the table")}
          onRetry={() => undefined}
        />
        <InlineError error={{ kind: "forbidden", requiredRole: "Admin" }} />
        <InlineError error={{ kind: "rate_limited", retryAfter: 30 }} />
      </Section>

      <Section title="EmptyState">
        <EmptyState
          kind="first-use"
          title={g("Create your first table")}
          description={g("Tables hold your app's data. Start with one, then add columns.")}
          action={<Button variant="primary">{g("Create table")}</Button>}
        />
        <EmptyState
          kind="no-results"
          title={g("No results")}
          description={g("No tables match these filters.")}
          action={<Button>{g("Clear filters")}</Button>}
        />
        <EmptyState
          kind="not-allowed"
          title={g("Storage isn't set up")}
          description={g("Add a storage bucket to upload files.")}
          action={<Button>{g("Set up storage")}</Button>}
        />
      </Section>

      <Section title="ErrorState">
        <ErrorState
          error={serverError}
          title={g("Couldn't load tables")}
          onRetry={() => undefined}
        />
        <ErrorState
          error={{ kind: "not_found" }}
          backLink={<Button>{g("Back to tables")}</Button>}
        />
        <ErrorState error={{ kind: "forbidden", requiredRole: "Admin" }} />
      </Section>

      <Section title="Skeletons">
        <div className="flex flex-col gap-2">
          <Skeleton width="1/2" height="title" />
          <Skeleton />
          <Skeleton width="3/4" />
        </div>
        <GridSkeleton columns={5} />
        <ContentSkeleton />
        <DelayedPendingDemo />
      </Section>

      <Section title="Progress and Spinner">
        <Progress label={g("Uploading backup.sql")} value={42} />
        <Progress label={g("Restoring backup")} />
        <div className="flex items-center gap-2">
          <Spinner aria-label={g("Loading tables")} />
          <span>{g("Spinner, inside small controls only")}</span>
        </div>
      </Section>

      <Section title="StatusChip">
        <div className="flex flex-wrap gap-2">
          {statuses.map((status) => (
            <StatusChip key={status} status={status} />
          ))}
        </div>
      </Section>

      <Section title="Badge">
        <div className="flex flex-wrap gap-2">
          {tones.map((tone) => (
            <Badge key={tone} tone={tone}>
              {tone}
            </Badge>
          ))}
          <Badge tone="info">12</Badge>
        </div>
      </Section>
    </Scene>
  );
}

/** useDelayedPending: nothing for 300 ms, then the skeleton for at least 500 ms (UX-21). */
function DelayedPendingDemo() {
  const g = useGalleryText();
  const [pending, setPending] = useState(false);
  const show = useDelayedPending(pending);
  const load = (ms: number) => {
    setPending(true);
    setTimeout(() => {
      setPending(false);
    }, ms);
  };
  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-2">
        <Button
          onPress={() => {
            load(200);
          }}
        >
          {g("Load in 200 ms")}
        </Button>
        <Button
          onPress={() => {
            load(1500);
          }}
        >
          {g("Load in 1.5 s")}
        </Button>
      </div>
      {show ? <GridSkeleton /> : <p className="text-fg-muted">{g("Loaded.")}</p>}
    </div>
  );
}
