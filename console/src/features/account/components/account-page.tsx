import { useLingui } from "@lingui/react/macro";
import { useQueryClient, useSuspenseQuery } from "@tanstack/react-query";
import { passwordFailure, sessionQuery, type Session } from "@/features/session";
import { ApiError } from "@/shared/api/errors";
import { accountLocale, activateLocale, hasLanguageChoice, shippedLocales } from "@/shared/i18n";
import { announceSessionChanged, signOutEverywhere } from "@/shared/session-channel";
import { Button, notify, PageHeader, SettingsCard } from "@/ui";
import { useAppForm } from "@/ui/form";
import { changePassword, useUpdateAccount } from "../api/mutations";
import {
  BROWSER_LOCALE,
  passwordSchema,
  profileSchema,
  toLocaleRequest,
  toPasswordRequest,
  toProfileRequest,
} from "../schemas/account";

type Operator = Session["operator"];

// Spec 0017 LOC-3: each language in its own script, so these are never translated.
// eslint-disable-next-line lingui/no-unlocalized-strings -- see above
const languageNames: Readonly<Record<string, string>> = { en: "English", ar: "العربية" };

/** Spec 0001 UX-22: what a refused save says, by what the server said. */
function useSaveFailure() {
  const { t } = useLingui();
  return (error: unknown) =>
    error instanceof ApiError && error.code === "version_conflict"
      ? t`Reload the page, then try again.`
      : t`Your changes weren't saved. Try again in a moment.`;
}

/** Spec 0017 ACC-3, AC-19: the profile, the language, the password, and sign out. */
export function AccountPage() {
  const { t } = useLingui();
  const { data: session } = useSuspenseQuery(sessionQuery());
  if (!session) return null; // `_shell`'s guard sends a signed out visit to sign in
  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t`Account`} />
      <ProfileCard operator={session.operator} />
      {hasLanguageChoice ? <LanguageCard operator={session.operator} /> : null}
      <PasswordCard />
      <SettingsCard
        title={t`Sign out`}
        footer={<Button onPress={() => void signOutEverywhere()}>{t`Sign out`}</Button>}
      >
        {null}
      </SettingsCard>
    </div>
  );
}

function ProfileCard({ operator }: { operator: Operator }) {
  const { t } = useLingui();
  const update = useUpdateAccount();
  const saveFailure = useSaveFailure();
  const form = useAppForm({
    defaultValues: { name: operator.name, email: operator.email },
    schema: profileSchema,
    onSubmitAsync: async ({ value }) => {
      try {
        await update.mutateAsync(toProfileRequest(profileSchema.parse(value)));
        return undefined;
      } catch (error) {
        const taken =
          error instanceof ApiError &&
          error.fieldErrors.some((field) => field.code === "not_unique");
        return taken
          ? { fields: { email: t`This email is taken.` } }
          : { form: saveFailure(error) };
      }
    },
  });
  return (
    <form.AppForm>
      <SettingsCard
        title={t`Profile`}
        footer={<form.SubmitButton isUpdate>{t`Update`}</form.SubmitButton>}
      >
        <form.Form>
          <form.ErrorSummary />
          <form.AppField name="name">
            {(field) => <field.TextField label={t`Name`} autoComplete="name" maxLength={128} />}
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
        </form.Form>
      </SettingsCard>
    </form.AppForm>
  );
}

function LanguageCard({ operator }: { operator: Operator }) {
  const { t } = useLingui();
  const update = useUpdateAccount();
  const saveFailure = useSaveFailure();
  const form = useAppForm({
    defaultValues: { locale: operator.locale ?? BROWSER_LOCALE },
    onSubmitAsync: async ({ value }) => {
      try {
        const saved = await update.mutateAsync(toLocaleRequest(value.locale));
        await activateLocale(accountLocale(saved.locale));
        return undefined;
      } catch (error) {
        return { form: saveFailure(error) };
      }
    },
  });
  return (
    <form.AppForm>
      <SettingsCard
        title={t`Language`}
        footer={<form.SubmitButton isUpdate>{t`Update`}</form.SubmitButton>}
      >
        <form.Form>
          <form.ErrorSummary />
          <form.AppField name="locale">
            {(field) => (
              <field.Select
                label={t`Language`}
                options={[
                  { id: BROWSER_LOCALE, label: t`Use my browser` },
                  ...shippedLocales.map((locale) => ({
                    id: locale,
                    label: languageNames[locale] ?? locale,
                  })),
                ]}
              />
            )}
          </form.AppField>
        </form.Form>
      </SettingsCard>
    </form.AppForm>
  );
}

function PasswordCard() {
  const { t } = useLingui();
  const queryClient = useQueryClient();
  const form = useAppForm({
    defaultValues: { currentPassword: "", newPassword: "" },
    schema: passwordSchema,
    onSubmitAsync: async ({ value, signal }) => {
      try {
        const session = await changePassword(
          toPasswordRequest(passwordSchema.parse(value)),
          signal,
        );
        queryClient.setQueryData(sessionQuery().queryKey, session);
      } catch (error) {
        if (signal.aborted) return undefined;
        const refused = error instanceof ApiError ? error.fieldErrors[0] : undefined;
        if (refused?.code === "invalid_password") {
          return { fields: { currentPassword: t`That password is wrong.` } };
        }
        if (refused?.field === "newPassword") {
          return { fields: { newPassword: passwordFailure(refused.code) } };
        }
        if (error instanceof ApiError && error.kind === "rate_limited") {
          return { form: t`Too many tries. Wait a moment, then try again.` };
        }
        return { form: t`Your changes weren't saved. Try again in a moment.` };
      }
      announceSessionChanged();
      notify({ tone: "success", message: t`Password changed`, source: t`Account` });
      form.reset(); // the saved values here are passwords, so the fields go back to empty
      return undefined;
    },
  });
  return (
    <form.AppForm>
      <SettingsCard
        title={t`Password`}
        description={t`This ends your other sessions.`}
        footer={<form.SubmitButton>{t`Change password`}</form.SubmitButton>}
      >
        <form.Form>
          <form.ErrorSummary />
          <form.AppField name="currentPassword">
            {(field) => (
              <field.PasswordField label={t`Current password`} autoComplete="current-password" />
            )}
          </form.AppField>
          <form.AppField name="newPassword">
            {(field) => (
              <field.PasswordField
                label={t`New password`}
                description={t`At least 8 characters. Common passwords are refused.`}
                autoComplete="new-password"
              />
            )}
          </form.AppField>
        </form.Form>
      </SettingsCard>
    </form.AppForm>
  );
}
