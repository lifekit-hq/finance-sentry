/**
 * Institution logo domain hints. We never store logo images ourselves — a
 * domain here is turned into a favicon URL by {@link InstitutionLogoUtils}.
 *
 * PROVIDER_DOMAINS: direct-connect providers where the provider code IS the
 * institution (Monobank, IBKR, Binance, Revolut X).
 *
 * BANK_NAME_DOMAINS: institutions identified by display name, not provider
 * code — aggregator-connected banks (e.g. TrueLayer) and rows that carry only
 * a bank name (Transactions), which also lists the direct-connect providers.
 * Matched by case-insensitive substring, so keep keywords lowercase.
 */
export const PROVIDER_DOMAINS: Readonly<Record<string, string>> = {
  monobank: 'monobank.ua',
  ibkr: 'interactivebrokers.com',
  binance: 'binance.com',
  // Provider slugs are the backend's snake_case wire values.
  // eslint-disable-next-line @typescript-eslint/naming-convention
  revolut_x: 'revolut.com',
};

export const BANK_NAME_DOMAINS: readonly (readonly [string, string])[] = [
  ['revolut', 'revolut.com'],
  ['allied irish', 'aib.ie'],
  ['aib', 'aib.ie'],
  ['monzo', 'monzo.com'],
  ['wise', 'wise.com'],
  ['starling', 'starlingbank.com'],
  // Rows that carry only a bank name (Transactions) have no provider code.
  ['monobank', 'monobank.ua'],
  ['interactive brokers', 'interactivebrokers.com'],
  ['binance', 'binance.com'],
];
