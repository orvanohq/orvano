import { useEffect, useState } from "react";
import { msg, plural } from "@lingui/core/macro";
import { useLingui } from "@lingui/react/macro";
import { Input, Label, TextField } from "react-aria-components";
import { announce } from "../announcer";
import { Button } from "../button";
import { Dialog } from "../dialog";
import { Icon } from "../icon";
import { ErrorIcon } from "../icons";
import { CheckboxControl } from "../form/choice-fields";
import { fieldColumn, textInput } from "../form/field";
import { dependentText, type Dependent, type LoadDependents } from "./dependents";
import { deleteLabels, tiers, type ResourceKind } from "./tiers";

// Spec 0015 PRM-10: imported by path (`@/ui/confirm-delete`) and loaded lazily where it first appears.

export type { Dependent, LoadDependents } from "./dependents";
export { tiers, type ResourceKind } from "./tiers";

interface CommonProps {
  isOpen: boolean;
  onOpenChange: (isOpen: boolean) => void;
  /** Deletes. A rejection keeps the dialog open with an inline error (UX-22); resolving closes it. */
  onConfirm: () => Promise<void>;
  /** Spec 0014 CPX-2: loads what goes with it, once each time the dialog opens; aborted when it closes. */
  loadDependents?: LoadDependents;
}

/** One resource of a kind in the tier map (UX-23). */
interface ResourceProps extends CommonProps {
  kind: ResourceKind;
  /** The resource's name, which tier 1 asks you to type. */
  name: string;
}

/** A single row: a short tier 2 confirm with "Don't ask again" for the table, which the caller remembers. */
interface RowProps extends CommonProps {
  kind: "row";
  dontAskAgain: boolean;
  onDontAskAgainChange: (dontAskAgain: boolean) => void;
}

/** Several rows: tier 2 stating the count; over 1,000 you type the count to confirm. */
interface RowsProps extends CommonProps {
  kind: "rows";
  count: number;
}

export type ConfirmDeleteProps = ResourceProps | RowProps | RowsProps;

/** Over this many rows, a bulk delete asks you to type the count (UX-23). */
const TYPE_COUNT_ABOVE = 1000;

type DependentsState =
  | { status: "loading" }
  | { status: "loaded"; items: Dependent[] }
  | { status: "failed" };

/**
 * design.md _Overlays_, spec 0001 UX-23: the delete confirmation, tiered by blast radius from `tiers.ts`. Tier 1
 * keeps its button unavailable until you type the exact name; tier 2 starts focus on Cancel. Both list what goes with
 * it while it loads, labeled "about" when estimated, and say when it couldn't be counted, still letting you delete.
 */
export function ConfirmDelete(props: ConfirmDeleteProps) {
  const { t, i18n } = useLingui();
  const { isOpen, onOpenChange, onConfirm, loadDependents } = props;
  const [typed, setTyped] = useState("");
  const [isDeleting, setDeleting] = useState(false);
  const [failed, setFailed] = useState(false);

  const openChange = (open: boolean) => {
    // UX-23: closing doesn't cancel a delete that has started, so don't pretend it does; its outcome shows here.
    if (!open && isDeleting) return;
    if (!open) {
      setTyped("");
      setFailed(false);
    }
    onOpenChange(open);
  };

  const confirmation = toConfirm(props);
  const label = deleteLabel(props);
  const isConfirmed =
    confirmation === undefined ||
    (confirmation.kind === "name"
      ? typed === confirmation.value
      : typed.replace(/\D/g, "") === String(confirmation.value));
  const typePrompt = confirmation ? String(confirmation.value) : "";
  const failure = t`Couldn't delete. Check your connection and try again.`;

  const remove = async () => {
    setDeleting(true);
    setFailed(false);
    try {
      await onConfirm();
    } catch {
      setFailed(true);
      announce(failure);
      return;
    } finally {
      setDeleting(false);
    }
    setTyped("");
    onOpenChange(false);
  };

  return (
    <Dialog
      role="alertdialog"
      title={i18n._(title(props))}
      isOpen={isOpen}
      onOpenChange={openChange}
      isDismissDisabled={isDeleting}
      footer={
        <>
          <Button slot="close" initialFocus={confirmation === undefined} isDisabled={isDeleting}>
            {t`Cancel`}
          </Button>
          <Button
            variant="danger"
            isPending={isDeleting}
            unavailableReason={isConfirmed ? undefined : confirmation.reason}
            onPress={() => {
              void remove();
            }}
          >
            {i18n._(label)}
          </Button>
        </>
      }
    >
      <p>{i18n._(description(props))}</p>
      {loadDependents ? <Dependents load={loadDependents} /> : null}
      {props.kind === "row" ? (
        <CheckboxControl
          label={t`Don't ask again for this table this session`}
          isSelected={props.dontAskAgain}
          onChange={props.onDontAskAgainChange}
        />
      ) : null}
      {confirmation ? (
        <TextField
          value={typed}
          onChange={setTyped}
          autoComplete="off"
          spellCheck="false"
          className={fieldColumn}
        >
          <Label className="font-medium">
            {confirmation.kind === "name"
              ? t`Type ${typePrompt} to confirm`
              : t`Type ${typePrompt} to confirm deleting the rows`}
          </Label>
          <Input className={textInput} />
        </TextField>
      ) : null}
      {failed ? (
        <p className="flex items-start gap-1.5 text-sm text-danger-text">
          <span className="mt-0.5">
            <Icon icon={ErrorIcon} />
          </span>
          {failure}
        </p>
      ) : null}
    </Dialog>
  );
}

/** Loads what goes with it when the dialog opens (it mounts with the dialog), and aborts when it closes. */
function Dependents({ load }: { load: LoadDependents }) {
  const { t, i18n } = useLingui();
  const [state, setState] = useState<DependentsState>({ status: "loading" });

  useEffect(() => {
    const controller = new AbortController();
    load(controller.signal).then(
      ({ items }) => {
        if (!controller.signal.aborted) setState({ status: "loaded", items });
        return undefined;
      },
      () => {
        if (!controller.signal.aborted) setState({ status: "failed" });
      },
    );
    return () => {
      controller.abort();
    };
  }, [load]);

  if (state.status === "loading") {
    return (
      <p aria-busy="true" className="text-fg-muted">
        {t`Counting what will be deleted with it…`}
      </p>
    );
  }
  if (state.status === "failed") {
    return (
      <p className="flex items-start gap-1.5 text-warning-text">
        <span className="mt-0.5">
          <Icon icon={ErrorIcon} />
        </span>
        {t`Couldn't count what will be deleted. You can still delete it.`}
      </p>
    );
  }
  const items = state.items.filter((item) => item.count > 0);
  if (items.length === 0) return null;
  return (
    <div className="flex flex-col gap-1">
      <p className="font-medium">{t`Also deleted:`}</p>
      <ul className="flex list-disc flex-col gap-0.5 ps-5">
        {items.map((item) => (
          <li key={item.kind}>{dependentText(i18n, item)}</li>
        ))}
      </ul>
    </div>
  );
}

/** What tier 1 and a large bulk delete ask you to type, or nothing for a plain confirm. */
function toConfirm(props: ConfirmDeleteProps) {
  if (props.kind === "row") return undefined;
  if (props.kind === "rows") {
    return props.count > TYPE_COUNT_ABOVE
      ? {
          kind: "count" as const,
          value: props.count,
          reason: msg`Type the number of rows to confirm.`,
        }
      : undefined;
  }
  return tiers[props.kind] === 1
    ? { kind: "name" as const, value: props.name, reason: msg`Type the name to confirm.` }
    : undefined;
}

function deleteLabel(props: ConfirmDeleteProps) {
  if (props.kind === "row") return msg`Delete row`;
  if (props.kind === "rows") {
    const { count } = props;
    return msg`${plural(count, { one: "Delete # row", other: "Delete # rows" })}`;
  }
  return deleteLabels[props.kind];
}

function title(props: ConfirmDeleteProps) {
  if (props.kind === "row") return msg`Delete this row?`;
  if (props.kind === "rows") {
    const { count } = props;
    return msg`${plural(count, { one: "Delete # row?", other: "Delete # rows?" })}`;
  }
  const { name } = props;
  return msg`Delete ${name}?`;
}

function description(props: ConfirmDeleteProps) {
  if (props.kind === "row" || props.kind === "rows") return msg`You can't undo this.`;
  const { name } = props;
  return tiers[props.kind] === 1
    ? msg`This deletes ${name} and everything in it. You can't undo this.`
    : msg`This deletes ${name}. You can't undo this.`;
}
