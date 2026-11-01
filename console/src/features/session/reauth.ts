import { ApiError, isSessionEnded } from "@/shared/api/errors";
import { reauthPrompt, sessionEnded } from "@/shared/stores/session";

function askForPassword(): Promise<boolean> {
  return new Promise((resolve) => {
    reauthPrompt.set((waiting) => [...waiting, resolve]);
  });
}

/** The prompt's answer, for every write waiting on it. */
export function answerReauth(confirmed: boolean): void {
  const waiting = reauthPrompt.get();
  reauthPrompt.set([]);
  for (const resolve of waiting) resolve(confirmed);
}

export function isReauthRequired(error: unknown): boolean {
  return error instanceof ApiError && error.code === "reauth_required";
}

/** Resolves once the sign in dialog has signed the same operator back in. */
function signedInAgain(): Promise<void> {
  return new Promise((resolve) => {
    const stop = sessionEnded.subscribe(() => {
      if (sessionEnded.get()) return;
      stop();
      resolve();
    });
    if (!sessionEnded.get()) {
      stop();
      resolve();
    }
  });
}

/**
 * Spec 0017 SES-8, SES-9, spec 0003 QRY-8: runs a write; on `reauth_required` it asks for the password, and on an ended
 * session it waits for the sign in dialog, then runs it once more. If the prompt is dismissed, or the second run is
 * refused too, the caller gets that error.
 */
export async function withReauth<Result>(write: () => Promise<Result>): Promise<Result> {
  try {
    return await write();
  } catch (error) {
    if (isSessionEnded(error)) await signedInAgain();
    else if (!isReauthRequired(error) || !(await askForPassword())) throw error;
  }
  return write();
}
