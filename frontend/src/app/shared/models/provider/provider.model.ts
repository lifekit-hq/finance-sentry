export type Provider = 'monobank' | 'binance' | 'revolut_x' | 'ibkr' | 'inzhur' | 'truelayer';

export type BankProvider = Extract<Provider, 'monobank' | 'truelayer'>;

export type CryptoProvider = Extract<Provider, 'binance' | 'revolut_x'>;

export type BrokerProvider = Extract<Provider, 'ibkr' | 'inzhur'>;

/** Providers chosen from the provider picker (every institution type has more than one). */
export type PickableProvider = BankProvider | CryptoProvider | BrokerProvider;

export type InstitutionType = 'bank' | 'crypto' | 'broker';

export type ProviderFormShape =
  | 'token'
  | 'key-secret'
  | 'key-private-key'
  | 'user-pass'
  | 'phone-password-sms'
  | 'open-banking-picker';

export interface ProviderDescriptor {
  readonly slug: Provider;
  readonly displayName: string;
  readonly institutionType: InstitutionType;
  readonly description: string;
  readonly iconAsset: string;
  readonly formShape: ProviderFormShape;
  readonly helpUrl: Nullable<string>;
}
