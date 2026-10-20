import {type Nullable} from '@lifekit-hq/ui';
export interface TrueLayerProvider {
  readonly providerId: string;
  readonly displayName: string;
  readonly country: string;
  readonly logoUrl: Nullable<string>;
}

export interface BeginTrueLayerConnectRequest {
  readonly providerId: string;
  readonly providerName: string;
}

export interface BeginTrueLayerConnectResponse {
  readonly link: string;
  readonly reference: string;
}
