import { lazy, Suspense, useState } from "react";
import { msg } from "@lingui/core/macro";
import { useLingui } from "@lingui/react/macro";
import { useSuspenseQuery } from "@tanstack/react-query";
import { useNavigate, useParams } from "@tanstack/react-router";
import { useCan } from "@/shared/access";
import { Button, DangerZone, DangerZoneItem, SettingsCard } from "@/ui";
import { useAppForm } from "@/ui/form";
import { loadProjectDependents, useDeleteProject, useUpdateProject } from "../api/mutations";
import { projectQuery, type Project } from "../api/queries";
import { renameProjectSchema, toRenameProjectRequest } from "../schemas/update-project";
import { saveFailure } from "./save-failure";

const ConfirmDelete = lazy(() =>
  import("@/ui/confirm-delete").then((module) => ({ default: module.ConfirmDelete })),
);

/** Spec 0017 PJT-5, PJT-7, CNS-1: the project's name, and deleting it. */
export function GeneralSettings() {
  const { projectId } = useParams({ from: "/_shell/$orgId/$projectId/settings/general" });
  const { data: project } = useSuspenseQuery(projectQuery(projectId));
  return (
    <div className="flex flex-col gap-6">
      <NameCard project={project} />
      <DeleteProject project={project} />
    </div>
  );
}

function NameCard({ project }: { project: Project }) {
  const { t } = useLingui();
  const update = useUpdateProject(project);
  const canWrite = useCan("project", "project.write");
  const form = useAppForm({
    defaultValues: { name: project.name },
    schema: renameProjectSchema,
    onSubmitAsync: async ({ value }) => {
      try {
        await update.mutateAsync(toRenameProjectRequest(renameProjectSchema.parse(value)));
        return undefined;
      } catch (error) {
        return { form: saveFailure(error) };
      }
    },
  });
  return (
    <form.AppForm>
      <SettingsCard
        title={t`General`}
        footer={<form.SubmitButton isUpdate>{t`Update`}</form.SubmitButton>}
      >
        <form.Form>
          <form.ErrorSummary />
          <form.AppField name="name">
            {(field) => <field.TextField label={t`Name`} maxLength={128} isReadOnly={!canWrite} />}
          </form.AppField>
        </form.Form>
      </SettingsCard>
    </form.AppForm>
  );
}

function DeleteProject({ project }: { project: Project }) {
  const { t } = useLingui();
  const { orgId } = useParams({ from: "/_shell/$orgId/$projectId/settings/general" });
  const navigate = useNavigate();
  const deleteProject = useDeleteProject(project);
  const canDelete = useCan("project", "project.delete");
  const [isConfirming, setConfirming] = useState(false);
  return (
    <DangerZone>
      <DangerZoneItem
        title={t`Delete project`}
        description={t`This removes the project, its keys, and everything in it. You can't undo this.`}
        action={
          <Button
            variant="danger"
            unavailableReason={
              canDelete
                ? project.status === "active"
                  ? undefined
                  : msg`This project is already being deleted.`
                : msg`You don't have permission to delete this project.`
            }
            onPress={() => {
              setConfirming(true);
            }}
          >
            {t`Delete project`}
          </Button>
        }
      />
      {isConfirming ? (
        <Suspense fallback={null}>
          <ConfirmDelete
            kind="project"
            name={project.name}
            isOpen
            onOpenChange={setConfirming}
            loadDependents={(signal) => loadProjectDependents(project.id, signal)}
            onConfirm={async () => {
              await deleteProject.mutateAsync();
              await navigate({ to: "/$orgId", params: { orgId } });
            }}
          />
        </Suspense>
      ) : null}
    </DangerZone>
  );
}
