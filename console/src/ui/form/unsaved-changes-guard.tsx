import { useLingui } from "@lingui/react/macro";
import { useBlocker } from "@tanstack/react-router";
import { useInteractionLock } from "@/shared/stores/interaction-lock";
import { Button } from "../button";
import { Dialog } from "../dialog";
import {
  affectedBy,
  discard,
  hasUnsavedChanges,
  useDiscardRequest,
  useHasUnsavedChanges,
} from "./unsaved-changes";

/**
 * Spec 0003 RTE-8, spec 0001 UX-9: the one user of the router's `useBlocker` (a lint rule keeps it so), rendered once
 * in the root route. A navigation that would unmount a changed form asks once, however many forms changed, with the
 * design system dialog, never `window.confirm`. Closing the browser tab gets the browser's own warning. While any
 * form has changes it holds the interaction lock (QRY-4).
 */
export function UnsavedChangesGuard() {
  const { t } = useLingui();
  const changed = useHasUnsavedChanges();
  useInteractionLock(changed);

  const blocker = useBlocker({
    shouldBlockFn: ({ next }) => affectedBy(next).length > 0,
    enableBeforeUnload: hasUnsavedChanges,
    withResolver: true,
  });
  const request = useDiscardRequest();

  const keep = () => {
    if (blocker.status === "blocked") blocker.reset();
    request?.resolve(false);
  };
  const leave = () => {
    if (blocker.status === "blocked") {
      discard(affectedBy(blocker.next));
      blocker.proceed();
    }
    request?.resolve(true);
  };

  return (
    <Dialog
      role="alertdialog"
      title={t`Discard unsaved changes?`}
      description={t`Your changes haven't been saved. If you leave now, they'll be lost.`}
      isOpen={blocker.status === "blocked" || request !== null}
      onOpenChange={(isOpen) => {
        if (!isOpen) keep();
      }}
      footer={
        <>
          <Button onPress={keep} initialFocus>
            {t`Keep editing`}
          </Button>
          <Button variant="danger" onPress={leave}>
            {t`Discard changes`}
          </Button>
        </>
      }
    />
  );
}
