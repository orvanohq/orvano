import { useState } from "react";
import { msg } from "@lingui/core/macro";
import { useLingui } from "@lingui/react/macro";
import { useSuspenseQuery } from "@tanstack/react-query";
import { useParams } from "@tanstack/react-router";
import { useCan } from "@/shared/access";
import { PAGE_SIZES } from "@/shared/url/paging";
import {
  Badge,
  Button,
  EmptyState,
  IconButton,
  Menu,
  MenuItem,
  MenuTrigger,
  MoreIcon,
  RelativeTime,
} from "@/ui";
import { Cell, Column, Pagination, Row, Table, TableBody, TableHeader } from "@/ui/table";
import { apiKeysQuery, type ApiKey } from "../api/queries";
import { CreateKeySheet } from "./create-key-sheet";
import { RegenerateKeyDialog, RenameKeyDialog, RevokeKeyDialog } from "./key-dialogs";
import { maskedKey } from "./masked-key";
import { useKeysView } from "./use-keys-view";

type KeyAction = "rename" | "revoke" | "regenerate";

const notAllowed = msg`You don't have permission to change keys.`;

/** Spec 0017 AKY-4 to AKY-8, AC-13, AC-14: a project's keys, the sheet that makes one, and what each key can do next. */
export function KeysPage() {
  const { t } = useLingui();
  const { projectId } = useParams({ from: "/_shell/$orgId/$projectId/settings/keys" });
  const { page, paging } = useKeysView();
  const { data } = useSuspenseQuery(apiKeysQuery(projectId, page));
  const { nextCursor } = data;
  const canWrite = useCan("project", "keys.write");
  const [isCreating, setCreating] = useState(false);
  const [acting, setActing] = useState<{ action: KeyAction; key: ApiKey }>();
  const create = (
    <Button
      variant="primary"
      unavailableReason={canWrite ? undefined : notAllowed}
      onPress={() => {
        setCreating(true);
      }}
    >
      {t`Create API key`}
    </Button>
  );
  const close = () => {
    setActing(undefined);
  };
  const dialog = acting && { projectId, apiKey: acting.key, onClose: close };

  return (
    <div className="flex flex-col gap-4">
      {data.items.length === 0 && page.cursor === undefined ? (
        <EmptyState
          kind="first-use"
          title={t`Create your first key`}
          description={t`A key lets your app reach this project. It's shown only once.`}
          action={create}
        />
      ) : (
        <>
          <div className="flex justify-end">{create}</div>
          <KeysTable
            keys={data.items}
            onAction={(action, key) => {
              setActing({ action, key });
            }}
          />
          <Pagination
            pageSize={paging.pageSize}
            pageSizes={PAGE_SIZES}
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
      {isCreating ? (
        <CreateKeySheet
          projectId={projectId}
          onClose={() => {
            setCreating(false);
          }}
        />
      ) : null}
      {dialog && acting.action === "rename" ? <RenameKeyDialog {...dialog} /> : null}
      {dialog && acting.action === "revoke" ? <RevokeKeyDialog {...dialog} /> : null}
      {dialog && acting.action === "regenerate" ? <RegenerateKeyDialog {...dialog} /> : null}
    </div>
  );
}

function KeysTable({
  keys,
  onAction,
}: {
  keys: readonly ApiKey[];
  onAction: (action: KeyAction, key: ApiKey) => void;
}) {
  const { t } = useLingui();
  return (
    <Table aria-label={t`API keys`}>
      <TableHeader>
        <Column id="name" isRowHeader>
          {t`Name`}
        </Column>
        <Column id="key">{t`Key`}</Column>
        <Column id="type">{t`Type`}</Column>
        <Column id="status">{t`Status`}</Column>
        <Column id="lastUsed">{t`Last used`}</Column>
        <Column id="expires">{t`Expires`}</Column>
        <Column id="created">{t`Created`}</Column>
        <Column id="actions">{t`Actions`}</Column>
      </TableHeader>
      <TableBody>
        {keys.map((key) => (
          <Row key={key.id} id={key.id}>
            <Cell>{key.name}</Cell>
            <Cell>
              <span className="font-mono">{maskedKey(key)}</span>
            </Cell>
            <Cell>{key.type === "secret" ? t`Secret` : t`Publishable`}</Cell>
            <Cell>
              <KeyStatus status={key.status} />
            </Cell>
            <Cell>
              {key.lastUsedAt ? (
                <RelativeTime datetime={key.lastUsedAt} focusable={false} />
              ) : (
                t`Never`
              )}
            </Cell>
            <Cell>
              {key.expiresAt ? (
                <RelativeTime datetime={key.expiresAt} focusable={false} />
              ) : (
                t`Never`
              )}
            </Cell>
            <Cell>
              <RelativeTime datetime={key.createdAt} focusable={false} />
            </Cell>
            <Cell>
              <KeyActions apiKey={key} onAction={onAction} />
            </Cell>
          </Row>
        ))}
      </TableBody>
    </Table>
  );
}

/** Spec 0001 UX-11, UX-30: a list's row actions go in a menu, and what you can't do stays visible with the reason. */
function KeyActions({
  apiKey,
  onAction,
}: {
  apiKey: ApiKey;
  onAction: (action: KeyAction, key: ApiKey) => void;
}) {
  const { t } = useLingui();
  const canWrite = useCan("api_key", "keys.write", apiKey);
  const { name } = apiKey;
  const whyNot = canWrite ? undefined : notAllowed;
  const whyNotRegenerate =
    whyNot ??
    (apiKey.status === "active" ? undefined : msg`Only an active key can be regenerated.`);
  const whyNotRevoke =
    whyNot ?? (apiKey.status === "revoked" ? msg`It's already revoked.` : undefined);
  return (
    <MenuTrigger
      trigger={<IconButton variant="ghost" icon={MoreIcon} aria-label={t`Actions for ${name}`} />}
    >
      <Menu aria-label={t`Actions for ${name}`}>
        <MenuItem
          id="rename"
          unavailableReason={whyNot}
          onAction={() => {
            onAction("rename", apiKey);
          }}
        >
          {t`Rename`}
        </MenuItem>
        <MenuItem
          id="regenerate"
          unavailableReason={whyNotRegenerate}
          onAction={() => {
            onAction("regenerate", apiKey);
          }}
        >
          {t`Regenerate`}
        </MenuItem>
        <MenuItem
          id="revoke"
          tone="danger"
          unavailableReason={whyNotRevoke}
          onAction={() => {
            onAction("revoke", apiKey);
          }}
        >
          {t`Revoke`}
        </MenuItem>
      </Menu>
    </MenuTrigger>
  );
}

function KeyStatus({ status }: { status: ApiKey["status"] }) {
  const { t } = useLingui();
  switch (status) {
    case "active":
      return <Badge tone="success">{t`Active`}</Badge>;
    case "expired":
      return <Badge tone="warning">{t`Expired`}</Badge>;
    default:
      return <Badge>{t`Revoked`}</Badge>;
  }
}
