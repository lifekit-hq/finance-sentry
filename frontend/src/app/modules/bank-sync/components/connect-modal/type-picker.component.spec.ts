import {signal} from '@angular/core';
import {TestBed} from '@angular/core/testing';
import {beforeEach, describe, expect, it, vi} from 'vitest';

import {type Provider} from '../../../../shared/models/provider/provider.model';
import {ConnectStore} from '../../store/connect/connect.store';
import {TypePickerComponent} from './type-picker.component';

function buildStore(connected: ReadonlySet<Provider> = new Set()) {
  return {
    connectedProviders: signal(connected),
    selectInstitutionType: vi.fn(),
    setModalStep: vi.fn(),
  };
}

function configure(store: ReturnType<typeof buildStore>): void {
  TestBed.configureTestingModule({providers: [{provide: ConnectStore, useValue: store}]});
}

describe('TypePickerComponent', () => {
  beforeEach(() => {
    TestBed.resetTestingModule();
  });

  it('connectionsByType counts providers by institution type from the store', () => {
    const store = buildStore(new Set<Provider>(['truelayer', 'monobank', 'binance', 'revolut_x']));
    configure(store);

    const fixture = TestBed.createComponent(TypePickerComponent);
    expect(fixture.componentInstance.connectionsByType()).toEqual({
      bank: 2,
      crypto: 2,
      broker: 0,
    });
  });

  it('select() forwards to store.selectInstitutionType', () => {
    const store = buildStore();
    configure(store);

    const fixture = TestBed.createComponent(TypePickerComponent);
    fixture.componentInstance.select('crypto');

    expect(store.selectInstitutionType).toHaveBeenCalledWith('crypto');
  });

  it('exposes three tiles for bank/crypto/broker', () => {
    configure(buildStore());
    const fixture = TestBed.createComponent(TypePickerComponent);
    expect(fixture.componentInstance.tiles.map(t => t.id)).toEqual(['bank', 'crypto', 'broker']);
  });
});
