export const MONOBANK_TOKEN_MIN_LENGTH = 30;
export const MONOBANK_TOKEN_MAX_LENGTH = 64;
export const MONOBANK_TOKEN_PATTERN = /^u[A-Za-z0-9_-]+$/;
export const MONOBANK_HELP_URL = 'https://api.monobank.ua';

export const BINANCE_HELP_URL = 'https://www.binance.com/en/support/faq/360002502072';

export const REVOLUT_X_HELP_URL =
  'https://developer.revolut.com/docs/x-api/revolut-x-crypto-exchange-rest-api';

export const PROVIDER_PICKER_PROMPT: Readonly<Record<'bank' | 'crypto', string>> = {
  bank: 'Choose your bank provider.',
  crypto: 'Choose your crypto exchange.',
};

export interface TrueLayerCountry {
  readonly code: string;
  readonly name: string;
  readonly flag: string;
}

export const TRUELAYER_COUNTRIES: readonly TrueLayerCountry[] = Object.freeze([
  Object.freeze({code: 'ie', name: 'Ireland', flag: '🇮🇪'}),
  Object.freeze({code: 'uk', name: 'United Kingdom', flag: '🇬🇧'}),
  Object.freeze({code: 'fr', name: 'France', flag: '🇫🇷'}),
  Object.freeze({code: 'es', name: 'Spain', flag: '🇪🇸'}),
  Object.freeze({code: 'de', name: 'Germany', flag: '🇩🇪'}),
  Object.freeze({code: 'nl', name: 'Netherlands', flag: '🇳🇱'}),
]);
