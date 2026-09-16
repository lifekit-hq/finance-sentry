import {type Provider, type ProviderDescriptor} from '../../models/provider/provider.model';

/**
 * Providers kept in the codebase but hidden from the connect picker.
 * Empty today; add a slug here to keep a provider wired but unlisted.
 */
export const HIDDEN_PROVIDERS: ReadonlySet<Provider> = new Set<Provider>();

export const PROVIDER_CATALOG: readonly ProviderDescriptor[] = Object.freeze([
  Object.freeze({
    slug: 'monobank',
    displayName: 'Monobank',
    institutionType: 'bank',
    description: 'Connect Monobank cards using a personal API token.',
    iconAsset: '/assets/providers/monobank.svg',
    formShape: 'token',
    helpUrl: 'https://api.monobank.ua',
  }),
  Object.freeze({
    slug: 'truelayer',
    displayName: 'Open Banking (EU / UK)',
    institutionType: 'bank',
    description: 'Connect Revolut, AIB, and most EU / UK banks via TrueLayer PSD2.',
    iconAsset: '/assets/providers/truelayer.svg',
    formShape: 'open-banking-picker',
    helpUrl: 'https://console.truelayer.com/',
  }),
  Object.freeze({
    slug: 'binance',
    displayName: 'Binance',
    institutionType: 'crypto',
    description: 'Connect a Binance account with a read-only API key and secret.',
    iconAsset: '/assets/providers/binance.svg',
    formShape: 'key-secret',
    helpUrl: 'https://www.binance.com/en/support/faq/360002502072',
  }),
  Object.freeze({
    slug: 'revolut_x',
    displayName: 'Revolut X',
    institutionType: 'crypto',
    description: 'Connect Revolut X with a read-only API key and its Ed25519 private key.',
    iconAsset: '/assets/providers/revolut_x.svg',
    formShape: 'key-private-key',
    helpUrl: 'https://developer.revolut.com/docs/x-api/revolut-x-crypto-exchange-rest-api',
  }),
  Object.freeze({
    slug: 'ibkr',
    displayName: 'Interactive Brokers',
    institutionType: 'broker',
    description: 'Connect an Interactive Brokers account via gateway credentials.',
    iconAsset: '/assets/providers/ibkr.svg',
    formShape: 'user-pass',
    helpUrl: 'https://www.interactivebrokers.com/en/trading/free-demo.php',
  }),
] as const);
