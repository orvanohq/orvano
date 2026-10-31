import { plural } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { ApiError } from "@/shared/api/errors";
import { Button, Dialog } from "@/ui";
import { useAppForm } from "@/ui/form";
import { confirmPassword } from "../api/mutations";
import { answerReauth } from "../reauth";
import { reauthSchema, toReauthRequest } from "../schemas/reauth";

/** Spec 0017 SES-8, AC-8: asks for the password. A wrong one fails on the field. */
// oxlint-disable-next-line import/no-default-export -- React.lazy takes a default export
export default function ReauthDialog() {
  const { t } = useLingui();

  function failureMessage(error: unknown): string {
    if (error instanceof ApiError && error.kind === "rate_limited") {
      if (error.retryAfter === undefined) return t`Too many tries. Wait a moment, then try again.`;
      const minutes = Math.ceil(error.retryAfter / 60);
      return plural(minutes, {
        one: "Too many tries. Try again in # minute.",
        other: "Too many tries. Try again in # minutes.",
      });
    }
    return t`Your password couldn't be checked. Try again in a moment.`;
  }

  const form = useAppForm({
    defaultValues: { password: "" },
    schema: reauthSchema,
    onSubmitAsync: async ({ value, signal }) => {
      try {
        await confirmPassword(toReauthRequest(reauthSchema.parse(value)), signal);
      } catch (error) {
        if (signal.aborted) return undefined;
        if (
          error instanceof ApiError &&
          error.fieldErrors.some((field) => field.code === "invalid_password")
        ) {
          return { fields: { password: t`That password is wrong.` } };
        }
        return { form: failureMessage(error) };
      }
      answerReauth(true);
      return undefined;
    },
  });

  return (
    <Dialog
      title={t`Confirm your password`}
      description={t`You haven't entered it for a while, and this action needs it.`}
      isOpen
      onOpenChange={(isOpen) => {
        if (!isOpen) answerReauth(false);
      }}
      footer={
        <form.AppForm>
          <Button slot="close">
            <Trans>Cancel</Trans>
          </Button>
          <form.SubmitButton>{t`Confirm`}</form.SubmitButton>
        </form.AppForm>
      }
    >
      <form.AppForm>
        <form.Form>
          <form.ErrorSummary />
          <form.AppField name="password">
            {(field) => (
              <field.PasswordField
                label={t`Password`}
                autoComplete="current-password"
                // oxlint-disable-next-line jsx-a11y/no-autofocus -- the prompt asks for this one thing, and it interrupts an action
                autoFocus
              />
            )}
          </form.AppField>
        </form.Form>
      </form.AppForm>
    </Dialog>
  );
}
