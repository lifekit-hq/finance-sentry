import {type InstitutionType} from '../../../../shared/models/provider/provider.model';

export type ModalStep =
  | 'type-picker'
  | 'provider-picker'
  | 'monobank-form'
  | 'truelayer-picker'
  | 'binance-form'
  | 'revolut-x-form'
  | 'ibkr-form'
  | 'inzhur-form'
  | 'closed';

/** One choice on the connect modal's first step: an institution type with its label and badge. */
export interface TypeTile {
  readonly id: InstitutionType;
  readonly label: string;
  readonly description: string;
  readonly badge: string;
}
