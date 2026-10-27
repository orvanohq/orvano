import { useState, type ReactNode } from "react";
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

// Spec 0015 GAL-2: feedback and states in every variant, on placeholder data (milestone 4, steps 17 and 18).

const tones: Tone[] = ["neutral", "info", "success", "warning", "danger"];
const statuses: JobStatus[] = ["queued", "running", "ready", "failed", "cancelled"];

const serverError = { kind: "server", requestId: "req_01JB8Z4Q7W2N" };

export function FeedbackScene() {
  return (
    <main className="mx-auto flex max-w-content flex-col gap-8 p-6">
      <h1 className="text-xl font-semibold">Feedback and states</h1>

      <Section title="notify and toasts">
        <p className="text-fg-muted">
          Three show at once, newest on top. Success closes after 6 seconds; errors stay. Raise the
          same one twice to see the count.
        </p>
        <div className="flex flex-wrap gap-2">
          <Button
            onPress={() => {
              notify({ tone: "success", message: "Table created", source: "Databases" });
            }}
          >
            Success
          </Button>
          <Button
            onPress={() => {
              notify({ tone: "info", message: "Backup started", source: "Backups" });
            }}
          >
            Info
          </Button>
          <Button
            onPress={() => {
              notify({ tone: "error", message: "Deploy failed", source: "Functions" });
            }}
          >
            Error
          </Button>
        </div>
      </Section>

      <Section title="Banner">
        {tones.map((tone) => (
          <Banner
            key={tone}
            tone={tone}
            title={`${tone.charAt(0).toUpperCase()}${tone.slice(1)} banner`}
            action={tone === "warning" ? <Button size="sm">Upgrade plan</Button> : undefined}
          >
            Storefront is at 90% of its database storage.
          </Banner>
        ))}
      </Section>

      <Section title="InlineError">
        <InlineError
          error={serverError}
          title="Couldn't save the table"
          onRetry={() => undefined}
        />
        <InlineError error={{ kind: "forbidden", requiredRole: "Admin" }} />
        <InlineError error={{ kind: "rate_limited", retryAfter: 30 }} />
      </Section>

      <Section title="EmptyState">
        <EmptyState
          kind="first-use"
          title="Create your first table"
          description="Tables hold your app's data. Start with one, then add columns."
          action={<Button variant="primary">Create table</Button>}
        />
        <EmptyState
          kind="no-results"
          title="No results"
          description="No tables match these filters."
          action={<Button>Clear filters</Button>}
        />
        <EmptyState
          kind="not-allowed"
          title="Storage isn't set up"
          description="Add a storage bucket to upload files."
          action={<Button>Set up storage</Button>}
        />
      </Section>

      <Section title="ErrorState">
        <ErrorState error={serverError} title="Couldn't load tables" onRetry={() => undefined} />
        <ErrorState error={{ kind: "not_found" }} backLink={<Button>Back to tables</Button>} />
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
        <Progress label="Uploading backup.sql" value={42} />
        <Progress label="Restoring backup" />
        <div className="flex items-center gap-2">
          <Spinner aria-label="Loading tables" />
          <span>Spinner, inside small controls only</span>
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
    </main>
  );
}

/** useDelayedPending: nothing for 300 ms, then the skeleton for at least 500 ms (UX-21). */
function DelayedPendingDemo() {
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
          Load in 200 ms
        </Button>
        <Button
          onPress={() => {
            load(1500);
          }}
        >
          Load in 1.5 s
        </Button>
      </div>
      {show ? <GridSkeleton /> : <p className="text-fg-muted">Loaded.</p>}
    </div>
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
