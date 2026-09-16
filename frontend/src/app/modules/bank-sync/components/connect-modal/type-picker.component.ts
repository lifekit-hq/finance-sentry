import {ChangeDetectionStrategy, Component, computed, inject} from '@angular/core';
import {SelectableCardComponent, TagComponent} from '@lifekit-hq/ui';

import {type Provider} from '../../../../shared/models/provider/provider.model';
import {type InstitutionType} from '../../../../shared/models/provider/provider.model';
import {ConnectStore} from '../../store/connect/connect.store';

interface TypeTile {
  readonly id: InstitutionType;
  readonly label: string;
  readonly description: string;
  readonly badge: string;
}

const TILES: readonly TypeTile[] = [
  {id: 'bank', label: 'Banking', description: 'Direct bank connections', badge: 'bank'},
  {id: 'crypto', label: 'Crypto', description: 'Exchange accounts', badge: 'binance'},
  {id: 'broker', label: 'Brokerage', description: 'Investment accounts', badge: 'ibkr'},
];

const PROVIDERS_FOR_TYPE: Record<InstitutionType, readonly Provider[]> = {
  bank: ['monobank', 'truelayer'],
  crypto: ['binance', 'revolut_x'],
  broker: ['ibkr'],
};

@Component({
  selector: 'fns-type-picker',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [TagComponent, SelectableCardComponent],
  templateUrl: './type-picker.component.html',
})
export class TypePickerComponent {
  public readonly store = inject(ConnectStore);

  public readonly tiles = TILES;

  public readonly connectionsByType = computed<Record<InstitutionType, number>>(() => {
    const connected = this.store.connectedProviders();
    return {
      bank: PROVIDERS_FOR_TYPE.bank.filter(s => connected.has(s)).length,
      crypto: PROVIDERS_FOR_TYPE.crypto.filter(s => connected.has(s)).length,
      broker: PROVIDERS_FOR_TYPE.broker.filter(s => connected.has(s)).length,
    };
  });

  public select(id: InstitutionType): void {
    this.store.selectInstitutionType(id);
  }
}
