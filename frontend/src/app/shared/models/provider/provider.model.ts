export type Provider = 'monobank' | 'binance' | 'revolut_x' | 'ibkr' | 'truelayer';

export type BankProvider = Extract<Provider, 'monobank' | 'truelayer'>;

export type CryptoProvider = Extract<Provider, 'binance' | 'revolut_x'>;

/** Providers chosen from the provider picker, as opposed to a type with a single provider. */
export type PickableProvider = BankProvider | CryptoProvider;

export type InstitutionType = 'bank' | 'crypto' | 'broker';

export type ProviderFormShape =
  'token' | 'key-secret' | 'key-private-key' | 'user-pass' | 'open-banking-picker';

export interface ProviderDescriptor {
  readonly slug: Provider;
  readonly displayName: string;
  readonly institutionType: InstitutionType;
  readonly description: string;
  readonly iconAsset: string;
  readonly formShape: ProviderFormShape;
  readonly helpUrl: Nullable<string>;
}
