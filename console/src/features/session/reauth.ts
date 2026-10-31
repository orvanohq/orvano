import { ApiError } from "@/shared/api/errors";
import { createStore } from "@/shared/stores/create-store";

/** The writes waiting on the password prompt, which `ReauthHost` shows while there are any. */
export const reauthPrompt = createStore<readonly ((confirmed: boolean) => void)[]>([]);

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

/**
 * Spec 0017 SES-8: runs a write; on `reauth_required` it asks for the password and runs it once more. If the prompt is
 * dismissed, or the second run is refused too, the caller gets that error.
 */
export async function withReauth<Result>(write: () => Promise<Result>): Promise<Result> {
  try {
    return await write();
  } catch (error) {
    if (!isReauthRequired(error) || !(await askForPassword())) throw error;
  }
  return write();
}
