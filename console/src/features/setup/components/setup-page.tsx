import { useEffect, useId, useRef, useState } from "react";
import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useQueryClient } from "@tanstack/react-query";
import { useRouter } from "@tanstack/react-router";
import { consoleConfigQuery } from "@/features/console-config";
import { ApiError } from "@/shared/api/errors";
import { takeFragmentParam } from "@/shared/url/link-token";
import { AuthLayout, Icon, SuccessIcon } from "@/ui";
import { useAppForm } from "@/ui/form";
import { checkSetupToken } from "../api/mutations";
import { setupTokenSchema, toSetupTokenCheckRequest } from "../schemas/setup-token";

/** The command that makes a new setup link (spec 0016 STP-5). Code, so it is never translated. */
const SETUP_TOKEN_COMMAND = "orvano admin setup-token";
/** The setting the console's address comes from (spec 0016 FRS-6, CON-2). A name, so never translated. */
const PUBLIC_URL_SETTING = "ORVANO_PUBLIC_URL";

/**
 * Spec 0016 FRS-4 to FRS-6: the first run setup page. Its first step checks the setup token; once the token is
 * accepted, the page keeps it in memory (never in storage, the URL, or a cache) for the owner account form that
 * feature 13 adds after it.
 */
export function SetupPage() {
  // Held for feature 13's owner form, which sends it again with the new account (STP-6).
  const [acceptedToken, setAcceptedToken] = useState<string>();
  const [linkToken, setLinkToken] = useState<string>();

  // FRS-5: the accepted state has no token step to read a link, so a link opened here is taken at this level. Only the
  // newest token works (STP-2, STP-5), so it replaces the accepted one and goes back through the token step.
  useEffect(() => {
    if (acceptedToken === undefined) return undefined;
    function replaceAcceptedToken() {
      const token = takeFragmentParam("token");
      if (!token) return;
      setLinkToken(token);
      setAcceptedToken(undefined);
    }
    // The token step's listener is gone by now, so a link that landed since is taken here first.
    replaceAcceptedToken();
    window.addEventListener("hashchange", replaceAcceptedToken);
    return () => {
      window.removeEventListener("hashchange", replaceAcceptedToken);
    };
  }, [acceptedToken]);

  return (
    <AuthLayout>
      <h1 className="text-xl font-semibold">
        <Trans>Set up Orvano</Trans>
      </h1>
      {acceptedToken === undefined ? (
        <TokenStep linkToken={linkToken} onAccepted={setAcceptedToken} />
      ) : (
        <TokenAccepted />
      )}
    </AuthLayout>
  );
}

function TokenStep({
  linkToken,
  onAccepted,
}: {
  linkToken: string | undefined;
  onAccepted: (token: string) => void;
}) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const router = useRouter();
  const [isChecking, setChecking] = useState(false);

  /** FRS-6's answers that aren't about the token itself, for the form's error summary (spec 0003 FRM-4). */
  function failureMessage(error: unknown): string {
    const kind = error instanceof ApiError ? error.kind : undefined;
    switch (kind) {
      case "rate_limited": {
        const retryAfter = error instanceof ApiError ? error.retryAfter : undefined;
        if (retryAfter === undefined) return t`Too many tries. Wait a moment, then try again.`;
        const seconds = Math.ceil(retryAfter);
        return plural(seconds, {
          one: "Too many tries. Try again in # second.",
          other: "Too many tries. Try again in # seconds.",
        });
      }
      case "forbidden":
        // CON-2: the server takes the check only from the console's own address.
        return t`Open the console at its own address, the one in ${PUBLIC_URL_SETTING}, not at an IP address or another name. Then use the setup link again.`;
      case "network":
      case "timeout":
        return t`Couldn't reach the server. Check your connection and try again.`;
      default:
        return t`The server had a problem. Try again in a moment.`;
    }
  }

  const form = useAppForm({
    defaultValues: { token: "" },
    schema: setupTokenSchema,
    onSubmitAsync: async ({ value, signal }) => {
      const request = toSetupTokenCheckRequest(setupTokenSchema.parse(value));
      setChecking(true);
      try {
        await checkSetupToken(request, signal);
      } catch (error) {
        if (signal.aborted) return undefined;
        if (
          error instanceof ApiError &&
          (error.code === "setup_token_invalid" || error.kind === "validation")
        ) {
          return {
            fields: {
              token: t`This setup link or token is not valid any more. Make a new one with ${SETUP_TOKEN_COMMAND}.`,
            },
          };
        }
        if (error instanceof ApiError && error.code === "setup_complete") {
          // Someone finished setup meanwhile: the fresh config sends this page to the console root (FRS-3).
          await queryClient.refetchQueries(consoleConfigQuery());
          await router.invalidate();
          return undefined;
        }
        return { form: failureMessage(error) };
      } finally {
        setChecking(false);
      }
      onAccepted(request.token);
      return undefined;
    },
  });

  // FRS-5: a setup link carries the token in its fragment. Take it once, before anything else can read the address,
  // and check it. The ref keeps StrictMode's second effect run from losing the token or checking it twice. A link
  // opened in a tab already on this page changes only the fragment, with no new page load, so a `hashchange` takes it
  // the same way. `linkToken` is a link SetupPage took while this step wasn't on the page.
  const linkRead = useRef(false);
  useEffect(() => {
    function checkToken(token: string | undefined) {
      if (token === undefined) return;
      form.setFieldValue("token", token);
      void form.handleSubmit();
    }
    function checkLinkToken() {
      checkToken(takeFragmentParam("token"));
    }
    if (!linkRead.current) {
      linkRead.current = true;
      checkToken(takeFragmentParam("token") ?? linkToken);
    }
    window.addEventListener("hashchange", checkLinkToken);
    return () => {
      window.removeEventListener("hashchange", checkLinkToken);
    };
  }, [form, linkToken]);

  return (
    <>
      <p className="text-fg-muted">
        <Trans>
          Paste the setup token from the install output or the server log, or make a new one on the
          server with <code className="font-mono text-fg">{SETUP_TOKEN_COMMAND}</code>.
        </Trans>
      </p>
      <form.AppForm>
        <form.Form>
          <form.ErrorSummary />
          <form.AppField name="token">
            {(field) => (
              <field.TextField
                label={t`Setup token`}
                // A one time secret: no saved values, no spell checking service, no autocorrect (security model).
                autoComplete="off"
                spellCheck="false"
                autoCorrect="off"
                maxLength={100}
                isReadOnly={isChecking}
              />
            )}
          </form.AppField>
          <form.SubmitButton>{t`Continue`}</form.SubmitButton>
        </form.Form>
      </form.AppForm>
    </>
  );
}

/** FRS-6's accepted state. Focus moves to its heading, so a screen reader hears the result. */
function TokenAccepted() {
  const headingId = useId();
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    headingRef.current?.focus();
  }, []);
  return (
    <section aria-labelledby={headingId} className="flex flex-col gap-2">
      <h2
        id={headingId}
        ref={headingRef}
        tabIndex={-1}
        className="flex items-center gap-2 font-semibold text-success-text outline-none"
      >
        <Icon icon={SuccessIcon} />
        <Trans>Setup token accepted</Trans>
      </h2>
      <p className="text-fg-muted">
        <Trans>
          The next step, creating the owner account, comes in a later version of the console. Keep
          this page open, or make a new setup link when you come back.
        </Trans>
      </p>
    </section>
  );
}
