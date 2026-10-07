/**
 * Inzhur has no API: the backend signs in to the owner's own cabinet the way the web cabinet does
 * (phone + password, then an SMS code) and keeps that session alive with a read-only daily sync.
 * Only the owner starts a sign-in, from this modal; the sync never sends an SMS on its own.
 */

/** Both omitted (null) to reuse the phone and password saved at the last successful sign-in. */
export interface StartInzhurLoginRequest {
  phone: Nullable<string>;
  password: Nullable<string>;
}

export interface VerifyInzhurLoginRequest {
  code: string;
}

export type InzhurLoginStatus = 'connected' | 'code_required' | 'invalid_code';

/** Where a sign-in step left the owner: signed in, or waiting for the SMS code. */
export interface InzhurConnectResult {
  status: InzhurLoginStatus;
  /** ISO timestamp the SMS code stops being accepted; set with `code_required`. */
  codeExpiresAt: Nullable<string>;
  /** Wrong codes still allowed; set with `invalid_code`. */
  attemptsLeft: Nullable<number>;
}

export type InzhurConnectionState = 'active' | 'reauth_required' | 'not_connected';

/** The owner's Inzhur connection; never carries a secret. */
export interface InzhurConnectionStatus {
  status: InzhurConnectionState;
  hasSavedCredentials: boolean;
  lastSyncAt: Nullable<string>;
  sessionStartedAt: Nullable<string>;
  /** False while the server's sign-in browser is not configured. */
  loginAvailable: boolean;
}
