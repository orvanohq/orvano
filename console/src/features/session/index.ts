// The session area's public surface (spec 0003 DIR-1): other areas import only from here.
export { connectSessionToken, sessionQuery, type Session } from "./api/queries";
export { signOut } from "./api/mutations";
export { requireSession, requireSignedOut, safeRedirect } from "./guards";
export { ReauthHost } from "./components/reauth-host";
export { isReauthRequired, withReauth } from "./reauth";
