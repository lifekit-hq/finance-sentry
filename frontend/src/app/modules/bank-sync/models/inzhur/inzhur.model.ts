/**
 * Inzhur has no API, and its sign-in rejects a server's IP with a bot check. So the owner signs in
 * on inzhur.reit in his own browser and pastes the session's refresh cookie here; the backend proves
 * it with one refresh and keeps that session alive with a read-only daily sync.
 */

/** The value of the `refreshToken` cookie on api.inzhur.reit. A secret: never logged or kept. */
export interface ConnectInzhurSessionRequest {
  refreshToken: string;
}

/** The pasted session refreshed and is stored. Failures come back as HTTP errors with a code. */
export interface InzhurConnectResult {
  status: 'connected';
}

export type InzhurConnectionState = 'active' | 'reauth_required' | 'not_connected';

/** The owner's Inzhur connection; never carries a secret. */
export interface InzhurConnectionStatus {
  status: InzhurConnectionState;
  lastSyncAt: Nullable<string>;
  sessionStartedAt: Nullable<string>;
}
