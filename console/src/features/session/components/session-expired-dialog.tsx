import { useLingui } from "@lingui/react/macro";
import { useQueryClient } from "@tanstack/react-query";
import { useRouter } from "@tanstack/react-router";
import { announceSessionChanged } from "@/shared/session-channel";
import { sessionEnded } from "@/shared/stores/session";
import { Dialog } from "@/ui";
import { useAppForm } from "@/ui/form";
import { signIn } from "../api/mutations";
import { sessionQuery } from "../api/queries";
import { signInSchema, toSignInRequest } from "../schemas/sign-in";
import { signInFailure } from "./sign-in-failure";

/**
 * Spec 0017 SES-9, spec 0003 QRY-8: the sign in dialog over the page, which keeps its forms. The same operator carries
 * on where they were: pages load again, and a write waiting in `withReauth` runs once more. Anyone else gets a full
 * reload, since the page holds the first operator's data.
 */
// oxlint-disable-next-line import/no-default-export -- React.lazy takes a default export
export default function SessionExpiredDialog() {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const router = useRouter();
  const held = queryClient.getQueryData(sessionQuery().queryKey);

  const form = useAppForm({
    defaultValues: { email: held?.operator.email ?? "", password: "" },
    schema: signInSchema,
    onSubmitAsync: async ({ value, signal }) => {
      let session;
      try {
        session = await signIn(toSignInRequest(signInSchema.parse(value)), signal);
      } catch (error) {
        if (signal.aborted) return undefined;
        return { form: signInFailure(error) };
      }
      if (session.operator.id !== held?.operator.id) {
        // The cookie is already theirs, so nothing on this page can be saved, and staying would show one operator's data
        // under another's session. `ignoreBlocker` keeps the unsaved changes guard from asking on the way out.
        await router.navigate({ to: "/", reloadDocument: true, ignoreBlocker: true });
        return undefined;
      }
      queryClient.setQueryData(sessionQuery().queryKey, session);
      sessionEnded.set(false);
      announceSessionChanged();
      await router.invalidate();
      await queryClient.refetchQueries({ predicate: (query) => query.state.status === "error" });
      return undefined;
    },
  });

  return (
    <Dialog
      title={t`Your session ended`}
      description={t`Sign in to carry on. What you typed is kept.`}
      isOpen
      isDismissDisabled
      footer={
        <form.AppForm>
          <form.SubmitButton>{t`Sign in`}</form.SubmitButton>
        </form.AppForm>
      }
    >
      <form.AppForm>
        <form.Form>
          <form.ErrorSummary />
          <form.AppField name="email">
            {(field) => <field.TextField label={t`Email`} type="email" autoComplete="username" />}
          </form.AppField>
          <form.AppField name="password">
            {(field) => (
              <field.PasswordField
                label={t`Password`}
                autoComplete="current-password"
                // oxlint-disable-next-line jsx-a11y/no-autofocus -- the dialog interrupts the page, and the email is filled in
                autoFocus
              />
            )}
          </form.AppField>
        </form.Form>
      </form.AppForm>
    </Dialog>
  );
}
