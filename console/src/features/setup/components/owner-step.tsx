import { useLingui } from "@lingui/react/macro";
import { useQueryClient } from "@tanstack/react-query";
import { useRouter } from "@tanstack/react-router";
import { consoleConfigQuery } from "@/features/console-config";
import { sessionQuery } from "@/features/session";
import { ApiError } from "@/shared/api/errors";
import { useAppForm } from "@/ui/form";
import { createOwner } from "../api/mutations";
import { ownerSchema, toSetupOwnerRequest, type OwnerValues } from "../schemas/owner";

/** What survives the token step coming back (OWN-6). */
export type OwnerDraft = Omit<OwnerValues, "password">;

function draftOf({ name, email, organizationName }: OwnerValues): OwnerDraft {
  return { name, email, organizationName };
}

/**
 * Spec 0017 OWN-5 to OWN-7: the owner form in the setup page's accepted state. It sends the token `SetupPage` holds. A
 * refused token sends the page back to the token step, keeping everything typed but the password.
 */
export function OwnerStep({
  token,
  draft,
  onDraftChange,
  onTokenRefused,
}: {
  token: string;
  draft: OwnerDraft;
  onDraftChange: (draft: OwnerDraft) => void;
  onTokenRefused: () => void;
}) {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const router = useRouter();

  const form = useAppForm({
    defaultValues: { ...draft, password: "" },
    schema: ownerSchema,
    onSubmitAsync: async ({ value, signal }) => {
      let session;
      try {
        session = await createOwner(toSetupOwnerRequest(ownerSchema.parse(value), token), signal);
      } catch (error) {
        if (signal.aborted) return undefined;
        if (error instanceof ApiError && error.code === "setup_token_invalid") {
          onDraftChange(draftOf(value));
          onTokenRefused();
          return undefined;
        }
        if (error instanceof ApiError && error.code === "setup_complete") {
          await queryClient.refetchQueries(consoleConfigQuery());
          await router.invalidate();
          return undefined;
        }
        const passwordError = error instanceof ApiError ? error.fieldErrors[0] : undefined;
        if (passwordError?.field === "password") {
          return {
            fields: {
              password:
                passwordError.code === "password_too_common"
                  ? t`This password is too common. Choose another.`
                  : passwordError.code === "too_long"
                    ? t`This password is too long.`
                    : t`Use at least 8 characters.`,
            },
          };
        }
        if (error instanceof ApiError && error.kind === "rate_limited") {
          return { form: t`Too many tries. Wait a moment, then try again.` };
        }
        return { form: t`The server had a problem. Try again in a moment.` };
      }

      // OWN-7.
      const first = session.memberships[0];
      queryClient.setQueryData(sessionQuery().queryKey, session);
      await queryClient.refetchQueries(consoleConfigQuery());
      await router.navigate({
        to: first ? "/$orgId" : "/",
        params: { orgId: first?.organizationId ?? "" },
        replace: true,
        ignoreBlocker: true, // saved, so the guard has nothing to ask
      });
      return undefined;
    },
  });

  return (
    <form.AppForm>
      <form.Form>
        <form.ErrorSummary />
        <form.AppField name="name">
          {(field) => <field.TextField label={t`Your name`} autoComplete="name" maxLength={128} />}
        </form.AppField>
        <form.AppField name="email">
          {(field) => (
            <field.TextField
              label={t`Email`}
              type="email"
              autoComplete="username"
              maxLength={254}
            />
          )}
        </form.AppField>
        <form.AppField name="organizationName">
          {(field) => <field.TextField label={t`Organization name`} maxLength={128} />}
        </form.AppField>
        <form.AppField name="password">
          {(field) => (
            <field.PasswordField
              label={t`Password`}
              description={t`At least 8 characters. Common passwords are refused.`}
              autoComplete="new-password"
            />
          )}
        </form.AppField>
        <form.SubmitButton>{t`Create owner account`}</form.SubmitButton>
      </form.Form>
    </form.AppForm>
  );
}
