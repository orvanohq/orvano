import { useEffect, useState } from "react";
import { Trans, useLingui } from "@lingui/react/macro";
import { useQueryClient, useSuspenseQuery } from "@tanstack/react-query";
import { useRouter } from "@tanstack/react-router";
import { consoleConfigQuery } from "@/features/console-config";
import { ApiError } from "@/shared/api/errors";
import { takeFragmentParam } from "@/shared/url/link-token";
import { AuthLayout, Banner, CodeSnippet, Link } from "@/ui";
import { useAppForm } from "@/ui/form";
import { useDocumentTitle } from "@/ui/shell/use-document-title";
import { resetPassword } from "../api/mutations";
import { sessionQuery } from "../api/queries";
import { passwordFailure } from "../password-failure";
import { resetSchema } from "../schemas/reset";

/** The command that makes a new reset link (spec 0017 RST-1). Code, so it is never translated. */
const RESET_COMMAND = "orvano admin reset-operator --email <email>";

/**
 * Spec 0017 RST-2, AC-18: takes the token from the link's fragment and drops it from the address (URL-5), then asks for a
 * new password. With no token, or a refused one, it shows the command that makes a link.
 */
export function ResetPage() {
  const { t } = useLingui();
  const { data: consoleConfig } = useSuspenseQuery(consoleConfigQuery());
  useDocumentTitle([{ kind: "page", label: t`Reset your password` }], {
    instanceName: consoleConfig.instanceName,
  });
  const [token, setToken] = useState(() => takeFragmentParam("token"));
  const [isRefused, setRefused] = useState(false);

  // A link opened on this page changes only the fragment, so nothing mounts again.
  useEffect(() => {
    function takeNewLink() {
      const next = takeFragmentParam("token");
      if (!next) return;
      setToken(next);
      setRefused(false);
    }
    window.addEventListener("hashchange", takeNewLink);
    return () => {
      window.removeEventListener("hashchange", takeNewLink);
    };
  }, []);

  return (
    <AuthLayout instanceName={consoleConfig.instanceName}>
      <h1 className="text-xl font-semibold">
        <Trans>Reset your password</Trans>
      </h1>
      {token && !isRefused ? (
        <ResetForm
          token={token}
          onRefused={() => {
            setRefused(true);
          }}
        />
      ) : (
        <>
          {isRefused ? <Banner tone="danger">{t`This link isn't valid any more.`}</Banner> : null}
          <p className="text-fg-muted">
            <Trans>Run this on the server to get a reset link.</Trans>
          </p>
          <CodeSnippet label={t`Reset your password`} code={RESET_COMMAND} />
          <Link to="/sign-in">
            <Trans>Sign in</Trans>
          </Link>
        </>
      )}
    </AuthLayout>
  );
}

function ResetForm({ token, onRefused }: { token: string; onRefused: () => void }) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const router = useRouter();

  const form = useAppForm({
    defaultValues: { password: "" },
    schema: resetSchema,
    onSubmitAsync: async ({ value, signal }) => {
      try {
        await resetPassword({ token, password: resetSchema.parse(value).password }, signal);
      } catch (error) {
        if (signal.aborted) return undefined;
        if (error instanceof ApiError && error.code === "reset_token_invalid") {
          onRefused();
          return undefined;
        }
        const refused = error instanceof ApiError ? error.fieldErrors[0] : undefined;
        if (refused?.field === "password") {
          return { fields: { password: passwordFailure(refused.code) } };
        }
        if (error instanceof ApiError && error.kind === "rate_limited") {
          return { form: t`Too many tries. Wait a moment, then try again.` };
        }
        return { form: t`The server had a problem. Try again in a moment.` };
      }
      // RST-3: every session of the operator ended, this browser's too if it had one.
      queryClient.setQueryData(sessionQuery().queryKey, null);
      await router.navigate({ to: "/sign-in", replace: true, ignoreBlocker: true });
      return undefined;
    },
  });

  return (
    <form.AppForm>
      <form.Form>
        <form.ErrorSummary />
        <form.AppField name="password">
          {(field) => (
            <field.PasswordField
              label={t`New password`}
              description={t`At least 8 characters. Common passwords are refused.`}
              autoComplete="new-password"
            />
          )}
        </form.AppField>
        <form.SubmitButton>{t`Change password`}</form.SubmitButton>
      </form.Form>
    </form.AppForm>
  );
}
