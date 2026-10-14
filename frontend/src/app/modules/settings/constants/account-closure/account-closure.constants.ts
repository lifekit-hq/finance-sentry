/** Provider keys only the user can revoke: closing the account deletes Finance Sentry's copy, not the key itself. */
export const PROVIDER_KEYS_TO_REVOKE: readonly string[] = [
  'Monobank personal token (api.monobank.ua)',
  'Binance and Revolut X API keys',
  'Interactive Brokers OAuth and Flex tokens',
  'Inzhur session (sign out of inzhur.reit everywhere)',
];
