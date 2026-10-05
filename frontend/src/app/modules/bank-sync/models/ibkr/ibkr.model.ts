/**
 * IBKR connect via OAuth 1.0a self-service. The user registers their own
 * consumer key + access token on IBKR's self-service OAuth portal and generates
 * three PEM artifacts locally; all six values are posted to
 * POST /brokerage/ibkr/connect. The backend stores the secret material encrypted
 * at rest (AES-256-GCM) and signs every IBKR request — no IBeam session, no 2FA.
 */
export interface ConnectIBKRRequest {
  consumerKey: string;
  accessToken: string;
  accessTokenSecret: string;
  /** Contents of private_signature.pem — RSA key that signs the live-session-token request. */
  signatureKey: string;
  /** Contents of private_encryption.pem — RSA key that decrypts the access token secret. */
  encryptionKey: string;
  /** Contents of dhparam.pem — Diffie-Hellman parameters seeding the live-session-token exchange. */
  dhParam: string;
}

export interface IBKRConnectResult {
  accountId: string;
  holdingsCount: number;
  connectedAt: string;
}

/**
 * IBKR connect via the Flex Web Service — the default path. Two values from the user's own
 * Client Portal: the Flex Web Service token and the id of an Activity Flex Query. Both are
 * posted to POST /brokerage/ibkr/flex/validate (dry run) and then /flex/connect (save).
 */
export interface ConnectIbkrFlexRequest {
  token: string;
  queryId: string;
}

/** What the Flex query returned for a not-yet-saved token + query id. */
export interface IbkrFlexPreview {
  accountId: string;
  /** ISO date (yyyy-MM-dd), or null when IBKR sent no parseable period. */
  fromDate: Nullable<string>;
  toDate: Nullable<string>;
  /** ISO timestamp of when IBKR generated the report, or null. */
  generatedAtUtc: Nullable<string>;
  openPositionsCount: number;
  cashCurrencies: string[];
  tradesCount: number;
  cashTransactionsCount: number;
}

/** The two ways to connect IBKR; the strategy routes on `kind`. */
export type IbkrConnectInput =
  {kind: 'flex'; payload: ConnectIbkrFlexRequest} | {kind: 'oauth'; payload: ConnectIBKRRequest};
