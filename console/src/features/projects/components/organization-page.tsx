import { useState } from "react";
import { Trans, useLingui } from "@lingui/react/macro";
import { useSuspenseQuery } from "@tanstack/react-query";
import { useNavigate, useParams } from "@tanstack/react-router";
import { ApiError } from "@/shared/api/errors";
import { Badge, Button, Dialog, EmptyState, Link, PageHeader } from "@/ui";
import { useAppForm } from "@/ui/form";
import { Cell, Column, Row, Table, TableBody, TableHeader } from "@/ui/table";
import { useCreateProject } from "../api/mutations";
import { projectsQuery, type Project } from "../api/queries";
import { createProjectSchema, toCreateProjectRequest } from "../schemas/create-project";

/** Spec 0017 CNS-5: an organization's projects, and "New project". */
export function OrganizationPage() {
  const { t } = useLingui();
  const { orgId } = useParams({ from: "/_shell/$orgId/" });
  const { data } = useSuspenseQuery(projectsQuery(orgId));
  const [isCreating, setCreating] = useState(false);
  const create = (
    <Button
      variant="primary"
      onPress={() => {
        setCreating(true);
      }}
    >
      {t`New project`}
    </Button>
  );

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t`Projects`} actions={data.items.length > 0 ? create : undefined} />
      {data.items.length === 0 ? (
        <EmptyState
          kind="first-use"
          title={t`Create your first project`}
          description={t`A project holds your data and the keys your apps use to reach it.`}
          action={create}
        />
      ) : (
        <ProjectsTable orgId={orgId} projects={data.items} />
      )}
      <NewProjectDialog orgId={orgId} isOpen={isCreating} onOpenChange={setCreating} />
    </div>
  );
}

function ProjectsTable({ orgId, projects }: { orgId: string; projects: readonly Project[] }) {
  const { t } = useLingui();
  return (
    <Table aria-label={t`Projects`}>
      <TableHeader>
        <Column id="name" isRowHeader>
          {t`Name`}
        </Column>
        <Column id="status">{t`Status`}</Column>
      </TableHeader>
      <TableBody>
        {projects.map((project) => (
          <Row key={project.id} id={project.id}>
            <Cell>
              <Link to="/$orgId/$projectId" params={{ orgId, projectId: project.id }}>
                {project.name}
              </Link>
            </Cell>
            <Cell>
              <ProjectStatus status={project.status} />
            </Cell>
          </Row>
        ))}
      </TableBody>
    </Table>
  );
}

function ProjectStatus({ status }: { status: Project["status"] }) {
  const { t } = useLingui();
  switch (status) {
    case "active":
      return <Badge tone="success">{t`Active`}</Badge>;
    case "creating":
      return <Badge tone="info">{t`Creating`}</Badge>;
    default:
      return <Badge tone="warning">{t`Deleting`}</Badge>;
  }
}

function NewProjectDialog({
  orgId,
  isOpen,
  onOpenChange,
}: {
  orgId: string;
  isOpen: boolean;
  onOpenChange: (isOpen: boolean) => void;
}) {
  const { t } = useLingui();
  const navigate = useNavigate();
  const createProject = useCreateProject(orgId);
  const form = useAppForm({
    defaultValues: { name: "", allowedOrigins: "" },
    schema: createProjectSchema,
    onSubmitAsync: async ({ value }) => {
      try {
        const project = await createProject.mutateAsync(
          toCreateProjectRequest(createProjectSchema.parse(value)),
        );
        onOpenChange(false);
        await navigate({
          to: "/$orgId/$projectId",
          params: { orgId, projectId: project.id },
          ignoreBlocker: true, // saved, so the guard has nothing to ask
        });
        return undefined;
      } catch (error) {
        if (
          error instanceof ApiError &&
          error.fieldErrors.some((field) => field.code === "invalid_origin")
        ) {
          return {
            fields: {
              allowedOrigins: t`Use exact origins such as https://app.example.com, one per line.`,
            },
          };
        }
        return { form: t`The project wasn't created. Try again in a moment.` };
      }
    },
  });

  return (
    <Dialog
      title={t`New project`}
      size="md"
      isOpen={isOpen}
      onOpenChange={onOpenChange}
      footer={
        <form.AppForm>
          <Button slot="close">
            <Trans>Cancel</Trans>
          </Button>
          <form.SubmitButton>{t`Create project`}</form.SubmitButton>
        </form.AppForm>
      }
    >
      <form.AppForm>
        <form.Form>
          <form.ErrorSummary />
          <form.AppField name="name">
            {(field) => <field.TextField label={t`Name`} maxLength={128} />}
          </form.AppField>
          <form.AppField name="allowedOrigins">
            {(field) => (
              <field.TextArea
                label={t`Allowed origins`}
                description={t`The browser origins your app runs on, one per line. You can change them later.`}
                isOptional
              />
            )}
          </form.AppField>
        </form.Form>
      </form.AppForm>
    </Dialog>
  );
}
