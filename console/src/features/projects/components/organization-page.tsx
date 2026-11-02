import { msg } from "@lingui/core/macro";
import { Trans, useLingui } from "@lingui/react/macro";
import { useQuery } from "@tanstack/react-query";
import { useNavigate, useParams } from "@tanstack/react-router";
import { NewOrganizationDialog } from "@/features/organizations";
import { useCan } from "@/shared/access";
import { ApiError } from "@/shared/api/errors";
import { keepListPage } from "@/shared/api/placeholder";
import { PAGE_SIZES } from "@/shared/url/paging";
import { Badge, Button, Dialog, EmptyState, Link, PageHeader, PageSkeleton } from "@/ui";
import { useAppForm } from "@/ui/form";
import { Cell, Column, Pagination, Row, Table, TableBody, TableHeader } from "@/ui/table";
import { useCreateProject } from "../api/mutations";
import { projectsQuery, type Project } from "../api/queries";
import { createProjectSchema, toCreateProjectRequest } from "../schemas/create-project";
import { useOrganizationView } from "./use-organization-view";

/** Spec 0003 QRY-10: how often the list looks again while a project is going away. */
const DELETING_POLL_MS = 2000;

/**
 * Spec 0017 CNS-5: an organization's projects, and "New project". The `create` search param opens either create dialog,
 * so a switcher can link to it.
 */
export function OrganizationPage() {
  const { t } = useLingui();
  const { orgId } = useParams({ from: "/_shell/$orgId/" });
  const { creating, open, page, paging } = useOrganizationView();
  const { data, isFetching, isPlaceholderData } = useQuery({
    ...projectsQuery(orgId, page),
    ...keepListPage(orgId),
    refetchInterval: (query) =>
      query.state.data?.items.some((project) => project.status === "deleting")
        ? DELETING_POLL_MS
        : false,
  });
  const canCreate = useCan("organization", "projects.create");
  const create = (
    <Button
      variant="primary"
      unavailableReason={
        canCreate ? undefined : msg`You don't have permission to create projects here.`
      }
      onPress={() => {
        open("project");
      }}
    >
      {t`New project`}
    </Button>
  );

  if (data === undefined) return <PageSkeleton />;
  const { nextCursor } = data;
  const isFirstUse = data.items.length === 0 && page.cursor === undefined && !isPlaceholderData;

  return (
    <div className="flex flex-col gap-6">
      <PageHeader title={t`Projects`} actions={isFirstUse ? undefined : create} />
      {isFirstUse ? (
        <EmptyState
          kind="first-use"
          title={t`Create your first project`}
          description={t`A project holds your data and the keys your apps use to reach it.`}
          action={create}
        />
      ) : (
        <>
          <ProjectsTable orgId={orgId} projects={data.items} isBusy={isFetching} />
          <Pagination
            pageSize={paging.pageSize}
            pageSizes={PAGE_SIZES}
            isPending={isPlaceholderData}
            onPageSizeChange={paging.setPageSize}
            onPrevious={paging.previous}
            onNext={
              nextCursor === null
                ? undefined
                : () => {
                    paging.next(nextCursor);
                  }
            }
          />
        </>
      )}
      <NewProjectDialog
        orgId={orgId}
        isOpen={creating === "project"}
        onOpenChange={(isOpen) => {
          open(isOpen ? "project" : undefined);
        }}
      />
      <NewOrganizationDialog
        isOpen={creating === "organization"}
        onOpenChange={(isOpen) => {
          open(isOpen ? "organization" : undefined);
        }}
      />
    </div>
  );
}

function ProjectsTable({
  orgId,
  projects,
  isBusy,
}: {
  orgId: string;
  projects: readonly Project[];
  isBusy: boolean;
}) {
  const { t } = useLingui();
  return (
    <Table aria-label={t`Projects`} isBusy={isBusy}>
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
              {project.status === "deleting" ? (
                project.name
              ) : (
                <Link to="/$orgId/$projectId" params={{ orgId, projectId: project.id }}>
                  {project.name}
                </Link>
              )}
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
