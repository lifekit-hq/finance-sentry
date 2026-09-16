/**
 * A read-only Revolut X API key and the Ed25519 private key (PKCS#8 PEM) whose public half was
 * registered with it.
 */
export interface ConnectRevolutXRequest {
  apiKey: string;
  privateKey: string;
}

export interface ConnectRevolutXResponse {
  message: string;
  holdingsCount: number;
  syncedAt: string;
}
