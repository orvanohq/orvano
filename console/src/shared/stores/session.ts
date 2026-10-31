import { createStore } from "./create-store";

/**
 * Spec 0003 QRY-8: a request found that the session has ended. The API client sets it, the shell opens the sign in
 * dialog, and signing in clears it.
 */
export const sessionEnded = createStore(false);

/** Spec 0017 SES-8: the writes waiting on the password prompt, which the shell shows while there are any. */
export const reauthPrompt = createStore<readonly ((confirmed: boolean) => void)[]>([]);
