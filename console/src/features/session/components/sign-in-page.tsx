import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useQueryClient, useSuspenseQuery } from "@tanstack/react-query";
import { useRouter, useSearch } from "@tanstack/react-router";
import { consoleConfigQuery } from "@/features/console-config";
import { ApiError } from "@/shared/api/errors";
import { AuthLayout } from "@/ui";
import { useDocumentTitle } from "@/ui/shell/use-document-title";
import { useAppForm } from "@/ui/form";
import { signIn } from "../api/mutations";
import { sessionQuery } from "../api/queries";
import { signInSchema, toSignInRequest } from "../schemas/sign-in";

/** Spec 0017 SES-3, AC-5: the sign in page. It never says which of the two was wrong (PWD-5). */
export function SignInPage() {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const router = useRouter();
  const { redirect: redirectTo } = useSearch({ from: "/_auth/sign-in" });
  const { data: consoleConfig } = useSuspenseQuery(consoleConfigQuery());
  useDocumentTitle([{ kind: "page", label: t`Sign in` }], {
    instanceName: consoleConfig.instanceName,
  });

  function failureMessage(error: unknown): string {
    const kind = error instanceof ApiError ? error.kind : undefined;
    switch (kind) {
      case "unauthenticated":
        return t`The email or password is wrong.`;
      case "rate_limited": {
        const retryAfter = error instanceof ApiError ? error.retryAfter : undefined;
        if (retryAfter === undefined) return t`Too many tries. Wait a moment, then try again.`;
        const minutes = Math.ceil(retryAfter / 60);
        return plural(minutes, {
          one: "Too many tries. Try again in # minute.",
          other: "Too many tries. Try again in # minutes.",
        });
      }
      case "forbidden":
        return t`Open the console at its own address, then try again.`;
      case "network":
      case "timeout":
        return t`Couldn't reach the server. Check your connection and try again.`;
      default:
        return t`The server had a problem. Try again in a moment.`;
    }
  }

  const form = useAppForm({
    defaultValues: { email: "", password: "" },
    schema: signInSchema,
    onSubmitAsync: async ({ value, signal }) => {
      try {
        const session = await signIn(toSignInRequest(signInSchema.parse(value)), signal);
        queryClient.setQueryData(sessionQuery().queryKey, session);
      } catch (error) {
        if (signal.aborted) return undefined;
        return { form: failureMessage(error) };
      }
      // The form is saved, so the unsaved changes guard has nothing to ask. `href` takes the path with its search, as
      // the guard saved it; the types only know `to`.
      const place = redirectTo ? { href: redirectTo } : { to: "/" };
      await router.navigate({ ...place, replace: true, ignoreBlocker: true } as never);
      return undefined;
    },
  });

  return (
    <AuthLayout instanceName={consoleConfig.instanceName}>
      <h1 className="text-xl font-semibold">
        <Trans>Sign in</Trans>
      </h1>
      <form.AppForm>
        <form.Form>
          <form.ErrorSummary />
          <form.AppField name="email">
            {(field) => <field.TextField label={t`Email`} type="email" autoComplete="username" />}
          </form.AppField>
          <form.AppField name="password">
            {(field) => <field.PasswordField label={t`Password`} autoComplete="current-password" />}
          </form.AppField>
          <form.SubmitButton>{t`Sign in`}</form.SubmitButton>
        </form.Form>
      </form.AppForm>
    </AuthLayout>
  );
}
