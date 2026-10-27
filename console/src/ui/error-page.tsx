import { useEffect } from "react";
import { useLingui } from "@lingui/react/macro";
import { reportConsoleError } from "@/shared/report";
import { Button } from "./button";
import { CopyField } from "./copy-field";
import { errorDetails } from "./error-copy";
import { Icon } from "./icon";
import { ErrorIcon, RetryIcon } from "./icons";
import { BrandMark } from "./shell/brand-mark";

interface ErrorPageProps {
  error: unknown;
  title: string;
  description: string;
}

/**
 * Spec 0003 FLT-2, spec 0015 AC-21: the plain page for when even the shell can't run. It needs no router, session,
 * or query client (the boot error page renders before them), only the tokens and the `en` catalog. It reports the
 * error once (FLT-4) and offers Reload and the request ID.
 */
function ErrorPage({ error, title, description }: ErrorPageProps) {
  const { t } = useLingui();
  const { requestId } = errorDetails(error);

  useEffect(() => {
    reportConsoleError(error);
  }, [error]);

  return (
    <div className="flex min-h-full flex-col items-center justify-center gap-6 bg-bg-subtle px-4 py-10">
      <header>
        <BrandMark />
      </header>
      <main
        id="main"
        className="flex w-full max-w-auth flex-col gap-4 rounded-lg border bg-bg-raised p-6 shadow-sm"
      >
        <div className="flex items-start gap-3">
          <span className="mt-1 text-danger-text">
            <Icon icon={ErrorIcon} size={20} />
          </span>
          <div className="flex flex-col gap-1">
            <h1 className="text-xl font-semibold text-fg">{title}</h1>
            <p className="text-fg-muted">{description}</p>
          </div>
        </div>
        {requestId ? <CopyField label={t`Request ID`} value={requestId} /> : null}
        <div className="flex justify-end">
          <Button
            variant="primary"
            onPress={() => {
              window.location.reload();
            }}
          >
            <Icon icon={RetryIcon} />
            {t`Reload`}
          </Button>
        </div>
      </main>
    </div>
  );
}

/** Spec 0003 RTE-10, FLT-2: rendered instead of the router when booting fails (the config or session request). */
export function BootErrorPage({ error }: { error: unknown }) {
  const { t } = useLingui();
  return (
    <ErrorPage
      error={error}
      title={t`Orvano couldn't start`}
      description={t`The console couldn't load what it needs from the server. Check that the server is running, then reload.`}
    />
  );
}

/** Spec 0003 FLT-2: the root route's error component, for an error no route below handled. */
export function RootErrorPage({ error }: { error: unknown }) {
  const { t } = useLingui();
  return (
    <ErrorPage
      error={error}
      title={t`Something went wrong`}
      description={t`This page ran into a problem. Reload to try again.`}
    />
  );
}
