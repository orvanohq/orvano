import { useLingui } from "@lingui/react/macro";
import { useSuspenseQuery } from "@tanstack/react-query";
import { useParams } from "@tanstack/react-router";
import { ApiError } from "@/shared/api/errors";
import { useCan } from "@/shared/access";
import { SettingsCard } from "@/ui";
import { useAppForm } from "@/ui/form";
import { useUpdateProject } from "../api/mutations";
import { projectQuery, type Project } from "../api/queries";
import { originList } from "../schemas/create-project";
import { originsSchema, toOriginsRequest } from "../schemas/update-project";
import { saveFailure } from "./save-failure";

/** Spec 0017 PJT-5, PJT-6, AC-11: the browser origins that may reach the project. */
export function OriginsSettings() {
  const { projectId } = useParams({ from: "/_shell/$orgId/$projectId/settings/origins" });
  const { data: project } = useSuspenseQuery(projectQuery(projectId));
  return <OriginsCard project={project} />;
}

function OriginsCard({ project }: { project: Project }) {
  const { t } = useLingui();
  const update = useUpdateProject(project);
  const canWrite = useCan("project", "project.write");
  const form = useAppForm({
    defaultValues: { allowedOrigins: project.allowedOrigins.join("\n") },
    schema: originsSchema,
    onSubmitAsync: async ({ value }) => {
      try {
        await update.mutateAsync(toOriginsRequest(originsSchema.parse(value)));
        return undefined;
      } catch (error) {
        const refused =
          error instanceof ApiError
            ? error.fieldErrors.find((field) => field.code === "invalid_origin")
            : undefined;
        if (!refused) return { form: saveFailure(error) };
        const index = Number(/\[(\d+)\]/.exec(refused.field)?.[1]);
        const origin = originList(value.allowedOrigins)[index];
        return {
          fields: {
            allowedOrigins: origin
              ? t`${origin} isn't an exact origin. Use one such as https://app.example.com, at most 100 of them.`
              : t`Use exact origins such as https://app.example.com, one per line.`,
          },
        };
      }
    },
  });
  return (
    <form.AppForm>
      <SettingsCard
        title={t`Allowed origins`}
        description={t`The browser origins your app runs on. A change applies within a minute.`}
        footer={<form.SubmitButton isUpdate>{t`Update`}</form.SubmitButton>}
      >
        <form.Form>
          <form.ErrorSummary />
          <form.AppField name="allowedOrigins">
            {(field) => (
              <field.TextArea
                label={t`Origins`}
                description={t`One per line, such as https://app.example.com.`}
                isOptional
                isReadOnly={!canWrite}
              />
            )}
          </form.AppField>
        </form.Form>
      </SettingsCard>
    </form.AppForm>
  );
}
