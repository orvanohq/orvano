// The session area's public surface (spec 0003 DIR-1): other areas import only from here.
export { connectSessionToken, sessionQuery, type Session } from "./api/queries";
export { passwordFailure } from "./password-failure";
export { requireSession, requireSignedOut, safeRedirect } from "./guards";
export { SessionPrompts } from "./components/session-prompts";
export { isReauthRequired, withReauth } from "./reauth";
