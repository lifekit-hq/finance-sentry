import {type ErrorMessagesMap} from '@lifekit-hq/core';

export const ERROR_MESSAGES_REGISTRY: ErrorMessagesMap = {
  GOOGLE_ACCOUNT_ONLY: "This account uses Google sign-in. Click 'Continue with Google' instead.",
  DUPLICATE_EMAIL: 'Email is already registered.',
  MONOBANK_TOKEN_INVALID: 'Invalid Monobank token. Please check and try again.',
  MONOBANK_TOKEN_DUPLICATE: 'This Monobank token is already connected.',
  MONOBANK_RATE_LIMITED: 'Monobank rate limit reached. Please wait 60 seconds and try again.',
  BINANCE_INVALID_CREDENTIALS:
    'Binance rejected the provided credentials. Use a read-only API key with no IP restrictions.',
  BINANCE_DUPLICATE:
    'Binance account already connected. Disconnect the existing one to use new keys.',
  BINANCE_ALREADY_CONNECTED:
    'Binance account already connected. Disconnect the existing one to use new keys.',
  REVOLUT_X_INVALID_CREDENTIALS:
    "Revolut X rejected the key pair. Check the API key belongs to the public key you registered, paste the Ed25519 private key (not the public one), and allow Finance Sentry's server IP if the key is IP-restricted.",
  REVOLUT_X_ALREADY_CONNECTED:
    'Revolut X account already connected. Disconnect the existing one to use a new key.',
  IBKR_INVALID_CREDENTIALS:
    'IB Gateway rejected the provided credentials. Confirm the 2FA push notification on your phone and try again.',
  IBKR_DUPLICATE: 'IBKR account already connected. Disconnect the existing one to reconnect.',
  IBKR_ALREADY_CONNECTED:
    'IBKR account already connected. Disconnect the existing one to reconnect.',
  IBKR_GATEWAY_UNAVAILABLE:
    'Could not reach the IBKR gateway. This is usually temporary — try again in a minute.',
  VALIDATION_ERROR: 'Some fields look wrong — please review the highlighted errors.',
  ALERT_NOT_FOUND: 'Alert not found.',
  ALERT_LOAD_FAILED: 'Failed to load alerts.',
  EVENTS_WINDOW_INVALID: 'The events window is invalid. Pick a range of at most a year.',
  BUDGET_NOT_FOUND: 'Budget not found.',
  BUDGET_DUPLICATE_CATEGORY: 'A budget for this category already exists.',
  BUDGET_INVALID_CATEGORY: 'Invalid budget category.',
  BUDGET_INVALID_LIMIT: 'Budget limit must be greater than zero.',
  BUDGET_INVALID_PERIOD: 'Invalid budget period.',
  SUBSCRIPTION_NOT_FOUND: 'Subscription not found.',
  LEDGER_READ_UNAVAILABLE: 'Ledger could not produce a read right now. Try again shortly.',
  // Feature 040 agent error codes are lowercase snake_case by contract (chat-endpoint.md).
  /* eslint-disable @typescript-eslint/naming-convention */
  agent_not_configured:
    'Ledger is not configured. An Anthropic API key must be set on the server to enable chat.',
  llm_unavailable: 'Ledger is temporarily unavailable. Please try again in a moment.',
  conversation_not_found: 'Conversation not found.',
  /* eslint-enable @typescript-eslint/naming-convention */
};
